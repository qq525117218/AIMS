using System.Collections.Concurrent; 
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
        _ = Task.Run(async () => 
        {
            try
            {
                // 定义进度回调
                Action<int, string> progressCallback = (percent, msg) =>
                {
                    status.Progress = percent;
                    status.Message = msg;
                    _redisService.SetAsync(redisKey, status, TimeSpan.FromMinutes(30)).Wait();
                };

                // 执行生成
                var fileBytes = await _psdService.CreatePsdFileAsync(request, progressCallback);

                // 保存文件到磁盘 (物理文件名保持 taskId 不变，避免特殊字符问题)
                string fileName = $"{taskId}.psd";
                string filePath = Path.Combine(TempFileDir, fileName);
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

                // 更新状态为完成
                status.Progress = 100;
                status.Status = "Completed";
                status.Message = "生成完成";

                // ✅ 核心修改：构造符合业务要求的文件名后缀
                // 格式要求：_“XxZxY”cm_YYMMDDHHMISS
                // 映射关系：X=Length, Z=Width, Y=Height
                var dim = request.Specifications.Dimensions;
                
                // 1. 规格部分: _10x5x15cm (长x宽x高)
                string sizePart = $"_{dim.Length}x{dim.Width}x{dim.Height}cm";
                
                // 2. 时间部分: _231201123055 (YYMMDDHHMISS -> yyMMddHHmmss)
                string timePart = $"_{DateTime.Now:yyMMddHHmmss}";

                // 3. 组合最终下载文件名
                string downloadName = $"{request.ProjectName}{sizePart}{timePart}.psd";

                // 设置下载接口 URL，并通过 fileName 参数传递给前端
                status.DownloadUrl = $"/api/design/download/{taskId}?fileName={downloadName}";
                
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

        // 加上 .psd 后缀确保浏览器识别
        if (!fileName.EndsWith(".psd", StringComparison.OrdinalIgnoreCase)) fileName += ".psd";
        
        // ✅ 修改建议：使用 PhysicalFile 替代 FileStream
        // PhysicalFile 自动处理断点续传 (Range 请求)、ETag 和文件流的高效传输
        return PhysicalFile(filePath, "application/x-photoshop", fileName);
    }
}