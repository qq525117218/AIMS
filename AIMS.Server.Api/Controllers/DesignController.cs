using System.Security.Cryptography; 
using System.Text;                  
using System.Text.Json;             
using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Psd;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
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
    /// 1. 提交生成任务 (支持严格幂等性：防止刷新导致重复生成)
    /// </summary>
    [HttpPost("generate/psd/async")]
    public async Task<ApiResponse<string>> SubmitPsdGeneration([FromBody] PsdRequestDto request)
    {
        if (!ModelState.IsValid) return ApiResponse<string>.Fail(400, "参数错误");

        // 1. 获取用户标识段 & 计算指纹
        string userSegment = request.UserContext != null 
            ? JsonSerializer.Serialize(request.UserContext) 
            : "anonymous";
        
        // 构建唯一指纹源 (参数 + 用户 = 唯一ID)
        string uniqueKeySource = $"{userSegment}:{request.ProjectName}:{JsonSerializer.Serialize(request.Specifications)}:{JsonSerializer.Serialize(request.Assets)}";
        string taskFingerprint = ComputeSha256Hash(uniqueKeySource);
        
        // 指纹映射 Key：用于存储 "参数指纹" -> "TaskId" 的映射关系
        string fingerprintRedisKey = $"task_lock:psd:{taskFingerprint}";
        
        // 数据有效期 (30分钟)，保持与 Task 状态有效期一致
        TimeSpan dataTtl = TimeSpan.FromMinutes(30);

        // ================== ✅ 修复：幂等性优先检查 ==================
        // 先检查是否已有对应任务 ID
        var existingTaskId = await _redisService.GetAsync<string>(fingerprintRedisKey);

        if (!string.IsNullOrEmpty(existingTaskId))
        {
            // 进一步检查：确保这个 TaskId 对应的任务状态是健康的
            // (防止 Redis 中指纹没过期，但任务状态数据意外丢失的情况)
            var taskStatusKey = $"task:psd:{existingTaskId}";
            var existingStatus = await _redisService.GetAsync<PsdTaskStatusDto>(taskStatusKey);

            if (existingStatus != null && existingStatus.Status != "Failed")
            {
                _logger.LogInformation($"[DesignController] 幂等性拦截：复用已存在任务 TaskId: {existingTaskId}");
                // 直接返回旧 ID，前端会自动轮询该 ID 的进度，不会触发新生成
                return ApiResponse<string>.Success(existingTaskId, "任务已存在");
            }
            else
            {
                // 如果旧任务失败了或者状态丢失，删除旧指纹，允许重新生成
                await _redisService.RemoveAsync(fingerprintRedisKey);
            }
        }

        // ================== ✅ 原子抢占并创建新任务 ==================
        string newTaskId = Guid.NewGuid().ToString("N");
        
        // 尝试建立映射：如果 Key 不存在则写入成功 (SetNx)
        // 这一步既是锁，也是持久化映射记录
        bool isLockAcquired = await _redisService.SetNxAsync(fingerprintRedisKey, newTaskId, dataTtl);

        if (!isLockAcquired)
        {
            // 极低概率并发：两个请求同时到达，上面 GetAsync 都为空，但其中一个 SetNx 成功了
            // 失败的那一方再次获取即可
            existingTaskId = await _redisService.GetAsync<string>(fingerprintRedisKey);
            if (!string.IsNullOrEmpty(existingTaskId))
            {
                 return ApiResponse<string>.Success(existingTaskId, "任务已由并发请求提交");
            }
            return ApiResponse<string>.Fail(409, "请求冲突，请稍后重试");
        }

        // ================== 开启后台任务 ==================
        string taskId = newTaskId; 
        string taskRedisKey = $"task:psd:{taskId}";

        // 初始化状态
        var status = new PsdTaskStatusDto 
        { 
            TaskId = taskId, 
            Status = "Processing", 
            Progress = 0, 
            Message = "任务已准备就绪" 
        };

        // 保存任务初始状态
        await _redisService.SetAsync(taskRedisKey, status, dataTtl);
        
        // 🔥 开启后台任务 (Fire-and-Forget)
        _ = Task.Run(async () => 
        {
            long lastUpdateTick = 0; // 用于节流

            try
            {
                // 定义进度回调
                Action<int, string> progressCallback = (percent, msg) =>
                {
                    // 状态机保护：进度不回退
                    if (percent < status.Progress) return;

                    status.Progress = percent;
                    status.Message = msg;

                    // 节流更新 Redis (每 300ms)
                    long now = DateTime.UtcNow.Ticks;
                    bool isImportantUpdate = percent >= 100 || percent == 0;
                    
                    if (isImportantUpdate || (now - lastUpdateTick) > TimeSpan.FromMilliseconds(300).Ticks)
                    {
                        lastUpdateTick = now;
                        // Fire-and-forget 保存状态
                        _redisService.SetAsync(taskRedisKey, status, dataTtl)
                            .ContinueWith(t => { 
                                if (t.IsFaulted) _logger.LogWarning($"[DesignController] 更新进度 Redis 失败: {t.Exception?.InnerException?.Message}"); 
                            });
                    }
                };

                // 1. 执行生成业务
                progressCallback(5, "正在初始化生成器...");
                var fileBytes = await _psdService.CreatePsdFileAsync(request, progressCallback);

                // 2. 保存文件到磁盘
                progressCallback(95, "正在保存文件...");
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
                
                // 确保最后一次状态必定写入
                await _redisService.SetAsync(taskRedisKey, status, dataTtl);

                // ✅ 重点：任务成功后，不要删除 fingerprintRedisKey！
                // 让它在 Redis 中保留 30 分钟。
                // 这样用户刷新页面时，会命中前面的 GetAsync，直接返回这个已完成的 TaskId，而不会触发新下载。
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DesignController] 后台生成 PSD 失败: {taskId}");
                
                status.Status = "Failed";
                status.Message = "生成失败: " + ex.Message;
                await _redisService.SetAsync(taskRedisKey, status, dataTtl);

                // ✅ 仅在失败时，才释放指纹锁，允许用户立即重试
                try 
                {
                    await _redisService.RemoveAsync(fingerprintRedisKey);
                    _logger.LogInformation($"[DesignController] 任务失败，已释放指纹锁: {fingerprintRedisKey}");
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "释放指纹锁失败");
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
        if (string.IsNullOrWhiteSpace(taskId) || taskId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || taskId.Contains("..")) 
            return BadRequest(ApiResponse<string>.Fail(400, "非法请求"));

        string filePath = Path.Combine(TempFileDir, $"{taskId}.psd");

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<string>.Fail(404, "文件已过期或不存在"));
        }

        if (string.IsNullOrWhiteSpace(fileName)) fileName = "download.psd";
        if (!fileName.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) fileName += ".psd";
        
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