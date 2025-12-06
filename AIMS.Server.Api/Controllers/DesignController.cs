using System.Security.Cryptography; 
using System.Text;                  
using System.Text.Json;             
using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Psd;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Interfaces; 
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DesignController : ControllerBase
{
    private readonly IPsdService _psdService;
    private readonly IRedisService _redisService; 
    private readonly ILogger<DesignController> _logger;

    private static readonly string TempFileDir = Path.Combine(Path.GetTempPath(), "AIMS_PSD_Files");

    public DesignController(IPsdService psdService, IRedisService redisService, ILogger<DesignController> logger)
    {
        _psdService = psdService;
        _redisService = redisService;
        _logger = logger;
        
        // 显式指定 System.IO.Directory 防止引用歧义
        if (!System.IO.Directory.Exists(TempFileDir)) 
        {
            System.IO.Directory.CreateDirectory(TempFileDir);
        }
    }

    /// <summary>
    /// 1. 提交生成任务 (支持幂等性：防止重复提交)
    /// </summary>
    [HttpPost("generate/psd/async")]
    public async Task<ApiResponse<string>> SubmitPsdGeneration([FromBody] PsdRequestDto request)
    {
        if (!ModelState.IsValid) return ApiResponse<string>.Fail(400, "参数错误");

        // 1. 获取用户标识段
        // ✅ 架构修复：直接序列化 UserContext 对象，不再依赖具体的属性名 (如 UserId)
        // 这样无论 UserContextDto 里定义的是 Id, UserName 还是 Token，都能正确生成唯一指纹
        string userSegment = request.UserContext != null 
            ? JsonSerializer.Serialize(request.UserContext) 
            : "anonymous";
        
        // 2. 计算任务指纹 (Task Fingerprint)
        // 指纹 = 用户信息 + 项目名 + 规格参数 + 素材参数
        // 任何一个参数变动，都会生成新的任务，否则视为重复提交
        string uniqueKeySource = $"{userSegment}:{request.ProjectName}:{JsonSerializer.Serialize(request.Specifications)}:{JsonSerializer.Serialize(request.Assets)}";
        string taskFingerprint = ComputeSha256Hash(uniqueKeySource);
        
        // 3. 检查 Redis 中是否已存在相同的任务
        string fingerprintRedisKey = $"task_lock:psd:{taskFingerprint}";
        var existingTaskId = await _redisService.GetAsync<string>(fingerprintRedisKey);

        if (!string.IsNullOrEmpty(existingTaskId))
        {
            // 进一步检查旧任务的状态
            var oldStatus = await _redisService.GetAsync<PsdTaskStatusDto>($"task:psd:{existingTaskId}");
            
            // 如果任务正在进行中 (Processing) 或者 刚刚完成 (Completed)，直接复用
            if (oldStatus != null && (oldStatus.Status == "Processing" || oldStatus.Status == "Completed"))
            {
                _logger.LogInformation($"[DesignController] 检测到重复提交，复用现有任务: {existingTaskId}");
                return ApiResponse<string>.Success(existingTaskId, "检测到相同的任务正在进行，已恢复进度监控");
            }
        }

        // ================== 开启新任务 ==================

        string taskId = Guid.NewGuid().ToString("N");
        string taskRedisKey = $"task:psd:{taskId}";

        // 初始化状态
        var status = new PsdTaskStatusDto 
        { 
            TaskId = taskId, 
            Status = "Processing", 
            Progress = 0, 
            Message = "任务已提交" 
        };

        // A. 保存任务状态 (有效期 30 分钟)
        await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
        
        // B. 设置指纹锁：将指纹映射到 TaskId (有效期 30 分钟)
        await _redisService.SetAsync(fingerprintRedisKey, taskId, TimeSpan.FromMinutes(30));

        // 🔥 开启后台任务 (Fire-and-Forget)
        _ = Task.Run(async () => 
        {
            try
            {
                Action<int, string> progressCallback = (percent, msg) =>
                {
                    status.Progress = percent;
                    status.Message = msg;
                    // 同步更新 Redis
                    _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30)).Wait();
                };

                // 执行生成
                var fileBytes = await _psdService.CreatePsdFileAsync(request, progressCallback);

                // 保存文件到磁盘
                string fileName = $"{taskId}.psd";
                string filePath = Path.Combine(TempFileDir, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

                // 更新状态为完成
                status.Progress = 100;
                status.Status = "Completed";
                status.Message = "生成完成";

                // 构造下载文件名
                var dim = request.Specifications.Dimensions;
                string sizePart = $"_{dim.Length}x{dim.Width}x{dim.Height}cm";
                string timePart = $"_{DateTime.Now:yyMMddHHmmss}";
                string downloadName = $"{request.ProjectName}{sizePart}{timePart}.psd";

                status.DownloadUrl = $"/api/design/download/{taskId}?fileName={downloadName}";
                
                await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DesignController] 后台生成 PSD 失败: {taskId}");
                status.Status = "Failed";
                status.Message = "生成失败: " + ex.Message;
                await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
                
                // ❌ 失败时移除指纹锁，允许用户立即重试
                await _redisService.RemoveAsync(fingerprintRedisKey);
            }
        });

        return ApiResponse<string>.Success(taskId, "任务已提交");
    }

    /// <summary>
    /// 2. 查询进度
    /// </summary>
    [HttpGet("progress/{taskId}")]
    public async Task<ApiResponse<PsdTaskStatusDto>> GetProgress(string taskId)
    {
        string redisKey = $"task:psd:{taskId}";
        var status = await _redisService.GetAsync<PsdTaskStatusDto>(redisKey);

        if (status == null) return ApiResponse<PsdTaskStatusDto>.Fail(404, "任务不存在或已过期");

        return ApiResponse<PsdTaskStatusDto>.Success(status);
    }

    /// <summary>
    /// 3. 下载文件
    /// </summary>
    [HttpGet("download/{taskId}")]
    public IActionResult DownloadPsd(string taskId, [FromQuery] string fileName = "download.psd")
    {
        // 安全检查
        if (taskId.Contains("..") || taskId.Contains("/") || taskId.Contains("\\")) 
            return BadRequest(ApiResponse<string>.Fail(400, "非法请求"));

        string filePath = Path.Combine(TempFileDir, $"{taskId}.psd");

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<string>.Fail(404, "文件已过期或不存在"));
        }

        if (!fileName.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) fileName += ".psd";
        
        // 使用 PhysicalFile 优化传输
        return PhysicalFile(filePath, "application/x-photoshop", fileName);
    }

    /// <summary>
    /// 辅助方法：计算 SHA256 哈希
    /// </summary>
    private static string ComputeSha256Hash(string rawData)
    {
        using (SHA256 sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}