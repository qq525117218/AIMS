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
    /// 1. 提交生成任务 (支持幂等性：防止并发重复提交)
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
        
        string fingerprintRedisKey = $"task_lock:psd:{taskFingerprint}";

        // ================== ✅ 修复点1：原子并发锁 ==================
        // 预先生成一个新的 TaskId
        string newTaskId = Guid.NewGuid().ToString("N");
        
        // 尝试抢锁：如果 Key 不存在，则设置成功并返回 true；否则返回 false
        // 这也是原子操作，彻底防止两个线程同时进入
        bool isLockAcquired = await _redisService.SetNxAsync(fingerprintRedisKey, newTaskId, TimeSpan.FromMinutes(30));

        if (!isLockAcquired)
        {
            // 没抢到锁，说明任务已存在。获取旧的 TaskId
            var existingTaskId = await _redisService.GetAsync<string>(fingerprintRedisKey);
            
            // 双重检查：如果锁还在但取不到 ID（极罕见），则允许继续
            if (!string.IsNullOrEmpty(existingTaskId))
            {
                // 检查旧任务状态，如果是处理中或已完成，直接返回旧 ID
                _logger.LogInformation($"[DesignController] 检测到重复提交，复用 TaskId: {existingTaskId}");
                return ApiResponse<string>.Success(existingTaskId, "任务已存在");
            }
        }

        // ================== 开启新任务 ==================
        
        // 如果抢到了锁，newTaskId 就是当前有效 ID
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
        await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
        
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

                    // ✅ 修复点2：移除 .Wait()，使用非阻塞异步更新
                    // 增加简单的节流机制（每 300ms 更新一次 Redis），防止高频 IO 拖慢生成速度
                    long now = DateTime.UtcNow.Ticks;
                    bool isImportantUpdate = percent >= 100 || percent == 0;
                    
                    if (isImportantUpdate || (now - lastUpdateTick) > TimeSpan.FromMilliseconds(300).Ticks)
                    {
                        lastUpdateTick = now;
                        // Fire-and-forget 保存状态，吞掉异常防止 Crash
                        _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30))
                            .ContinueWith(t => { 
                                if (t.IsFaulted) _logger.LogWarning($"[DesignController] 更新进度 Redis 失败: {t.Exception?.InnerException?.Message}"); 
                            });
                    }
                };

                // 1. 执行生成业务 (Aspose 生成器负责 0% - 90%)
                progressCallback(5, "正在初始化生成器...");
                var fileBytes = await _psdService.CreatePsdFileAsync(request, progressCallback);

                // 2. 保存文件到磁盘 (Controller 负责 90% - 95%)
                progressCallback(92, "正在保存文件...");
                string fileName = $"{taskId}.psd";
                string filePath = Path.Combine(TempFileDir, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

                // 3. 构造下载信息
                var dim = request.Specifications.Dimensions;
                string sizePart = $"_{dim.Length}x{dim.Width}x{dim.Height}cm";
                string timePart = $"_{DateTime.Now:yyMMddHHmmss}";
                string downloadName = $"{request.ProjectName}{sizePart}{timePart}.psd";

                // 4. 更新最终状态 (100%)
                status.Progress = 100;
                status.Status = "Completed";
                status.Message = "生成完成";
                status.DownloadUrl = $"/api/design/download/{taskId}?fileName={downloadName}";
                
                // 确保最后一次状态必定写入
                await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[DesignController] 后台生成 PSD 失败: {taskId}");
                
                status.Status = "Failed";
                status.Message = "生成失败: " + ex.Message;
                await _redisService.SetAsync(taskRedisKey, status, TimeSpan.FromMinutes(30));
            }
            finally
            {
                // 任务结束，释放指纹锁
                try 
                {
                    await _redisService.RemoveAsync(fingerprintRedisKey);
                    _logger.LogInformation($"[DesignController] 任务结束，已释放指纹锁: {fingerprintRedisKey}");
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