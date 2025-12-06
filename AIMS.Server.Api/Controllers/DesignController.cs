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
        
        if (!System.IO.Directory.Exists(TempFileDir)) 
        {
            System.IO.Directory.CreateDirectory(TempFileDir);
        }
    }

    /// <summary>
    /// 1. 提交生成任务 (支持幂等性：防止并发重复提交，但允许完成后重新生成)
    /// </summary>
    [HttpPost("generate/psd/async")]
    public async Task<ApiResponse<string>> SubmitPsdGeneration([FromBody] PsdRequestDto request)
    {
        if (!ModelState.IsValid) return ApiResponse<string>.Fail(400, "参数错误");

        // 1. 获取用户标识段 & 计算指纹
        string userSegment = request.UserContext != null 
            ? JsonSerializer.Serialize(request.UserContext) 
            : "anonymous";
        
        // 构建唯一指纹源
        string uniqueKeySource = $"{userSegment}:{request.ProjectName}:{JsonSerializer.Serialize(request.Specifications)}:{JsonSerializer.Serialize(request.Assets)}";
        string taskFingerprint = ComputeSha256Hash(uniqueKeySource);
        
        // 2. 检查 Redis 锁 (并发控制)
        string fingerprintRedisKey = $"task_lock:psd:{taskFingerprint}";
        var existingTaskId = await _redisService.GetAsync<string>(fingerprintRedisKey);

        if (!string.IsNullOrEmpty(existingTaskId))
        {
            // 检查旧任务状态
            var oldStatus = await _redisService.GetAsync<PsdTaskStatusDto>($"task:psd:{existingTaskId}");
            
            // 优化逻辑：只有任务正在 "Processing" 时才拦截
            // 如果任务已完成 (Completed/Failed)，我们允许指纹锁失效（或被清理），从而允许新任务生成
            // 但为了安全起见，这里做一个双重检查：如果锁还在，且状态是 Processing，才视为重复提交
            if (oldStatus != null && oldStatus.Status == "Processing")
            {
                _logger.LogInformation($"[DesignController] 检测到正在进行的任务，复用 TaskId: {existingTaskId}");
                return ApiResponse<string>.Success(existingTaskId, "任务正在进行中，请等待完成");
            }
            // 如果锁存在但任务已完成/失败，说明之前的 finally 清理可能失败了，或者是过期时间重叠
            // 这里我们选择忽略旧锁，继续执行新任务
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
            Message = "任务已准备就绪" 
        };

        // A. 保存任务状态 (有效期 30 分钟)
        await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
        
        // B. 设置指纹锁 (有效期 30 分钟 - 作为兜底，正常情况会在 finally 中移除)
        await _redisService.SetAsync(fingerprintRedisKey, taskId, TimeSpan.FromMinutes(30));

        // 🔥 开启后台任务 (Fire-and-Forget)
        // 注意：在生产环境中，建议使用 IServiceScopeFactory 创建独立作用域，防止 Scoped 服务已被释放
        _ = Task.Run(async () => 
        {
            try
            {
                // 定义进度回调
                Action<int, string> progressCallback = (percent, msg) =>
                {
                    // 仅当进度有实质变化时才更新 Redis，减少网络 IO (可选优化)
                    if (status.Progress != percent) 
                    {
                        status.Progress = percent;
                        status.Message = msg;
                        _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30)).Wait();
                    }
                };

                // 1. 执行生成业务
                progressCallback(10, "正在初始化生成器...");
                var fileBytes = await _psdService.CreatePsdFileAsync(request, progressCallback);

                // 2. 保存文件到磁盘
                progressCallback(90, "正在保存文件...");
                string fileName = $"{taskId}.psd";
                string filePath = Path.Combine(TempFileDir, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

                // 3. 构造下载信息
                var dim = request.Specifications.Dimensions;
                string sizePart = $"_{dim.Length}x{dim.Width}x{dim.Height}cm";
                string timePart = $"_{DateTime.Now:yyMMddHHmmss}";
                string downloadName = $"{request.ProjectName}{sizePart}{timePart}.psd";

                // 4. 更新最终状态
                status.Progress = 100;
                status.Status = "Completed";
                status.Message = "生成完成";
                status.DownloadUrl = $"/api/design/download/{taskId}?fileName={downloadName}";
                
                await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DesignController] 后台生成 PSD 失败: {taskId}");
                
                status.Status = "Failed";
                status.Message = "生成失败: " + ex.Message;
                // 失败状态也保留，以便前端查询原因
                await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
            }
            finally
            {
                // ✅ 核心修复：无论成功还是失败，任务结束时必须移除指纹锁
                // 这确保了下一次相同的请求可以重新触发生成
                try 
                {
                    await _redisService.RemoveAsync(fingerprintRedisKey);
                    _logger.LogInformation($"[DesignController] 任务结束，已释放指纹锁: {fingerprintRedisKey}");
                }
                catch (Exception cleanupEx)
                {
                    // 即使 Redis 连接异常，也不能让异常抛出导致 Crash，只记录日志
                    _logger.LogWarning(cleanupEx, $"[DesignController] 释放指纹锁失败: {fingerprintRedisKey}");
                }
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
        // 安全检查：防止目录遍历攻击
        if (string.IsNullOrWhiteSpace(taskId) || taskId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || taskId.Contains("..")) 
            return BadRequest(ApiResponse<string>.Fail(400, "非法请求"));

        string filePath = Path.Combine(TempFileDir, $"{taskId}.psd");

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<string>.Fail(404, "文件已过期或不存在"));
        }

        // 规范化文件名
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download.psd";
        if (!fileName.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) fileName += ".psd";
        
        // 使用 PhysicalFile 优化大文件传输
        return PhysicalFile(filePath, "application/x-photoshop", fileName);
    }

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