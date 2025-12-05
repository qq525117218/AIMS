using System.Collections.Concurrent; // 用于简单缓存，生产环境建议用 Redis
using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Psd;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Interfaces; // 为了使用 IRedisService
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DesignController : ControllerBase
{
    private readonly IPsdService _psdService;
    private readonly IRedisService _redisService; // 注入 Redis 用于存进度
    private readonly ILogger<DesignController> _logger;

    // 建议把文件存到临时目录，Redis只存路径，避免 Redis 内存爆炸
    private static readonly string TempFileDir = Path.Combine(Path.GetTempPath(), "AIMS_PSD_Files");

    public DesignController(IPsdService psdService, IRedisService redisService, ILogger<DesignController> logger)
    {
        _psdService = psdService;
        _redisService = redisService;
        _logger = logger;
        
        if (!Directory.Exists(TempFileDir)) Directory.CreateDirectory(TempFileDir);
    }

    /// <summary>
    /// 1. 提交生成任务 (异步)
    /// </summary>
    /// <returns>返回任务 ID</returns>
    [HttpPost("generate/psd/async")]
    public async Task<ApiResponse<string>> SubmitPsdGeneration([FromBody] PsdRequestDto request)
    {
        if (!ModelState.IsValid) return ApiResponse<string>.Fail(400, "参数错误");

        // 生成唯一任务ID
        string taskId = Guid.NewGuid().ToString("N");
        string redisKey = $"task:psd:{taskId}";

        // 初始化状态
        var status = new PsdTaskStatusDto { TaskId = taskId, Status = "Processing", Progress = 0, Message = "任务已提交" };
        await _redisService.SetAsync(redisKey, status, TimeSpan.FromMinutes(30));

        // 🔥 核心：开启后台任务 (Fire-and-Forget)
        // 注意：在实际高并发生产环境中，建议使用 Hangfire 或 RabbitMQ，这里用 Task.Run 演示最简方案
        _ = Task.Run(async () => 
        {
            try
            {
                // 定义进度回调
                Action<int, string> progressCallback = (percent, msg) =>
                {
                    // 优化：避免过于频繁写入 Redis
                    status.Progress = percent;
                    status.Message = msg;
                    _redisService.SetAsync(redisKey, status, TimeSpan.FromMinutes(30)).Wait();
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
                // 设置下载接口的相对路径 (假设前端拼 BaseUrl)
                status.DownloadUrl = $"/api/design/download/{taskId}?fileName={request.ProjectName}.psd";
                
                await _redisService.SetAsync(redisKey, status, TimeSpan.FromMinutes(30));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台生成 PSD 失败");
                status.Status = "Failed";
                status.Message = "生成失败: " + ex.Message;
                await _redisService.SetAsync(redisKey, status, TimeSpan.FromMinutes(30));
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
        // 安全检查：防止目录遍历
        if (taskId.Contains("..") || taskId.Contains("/")) return BadRequest("非法请求");

        string filePath = Path.Combine(TempFileDir, $"{taskId}.psd");

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<string>.Fail(404, "文件已过期或不存在"));
        }

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        // 加上 .psd 后缀确保浏览器识别
        if (!fileName.EndsWith(".psd")) fileName += ".psd";
        
        return File(fileStream, "application/x-photoshop", fileName);
    }
}