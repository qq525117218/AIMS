using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Psd;
using AIMS.Server.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DesignController : ControllerBase
{
    private readonly IPsdService _psdService;
    private readonly ILogger<DesignController> _logger;

    public DesignController(IPsdService psdService, ILogger<DesignController> logger)
    {
        _psdService = psdService;
        _logger = logger;
    }

    /// <summary>
    /// 生成 Photoshop (PSD) 模板文件
    /// </summary>
    /// <remarks>
    /// 接受包含规格和内容的复杂对象，生成分图层的 PSD 文件。
    /// </remarks>
    [HttpPost("generate/psd")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GeneratePsd([FromBody] PsdRequestDto request)
    {
        // 模型绑定校验 (Dimensions 中的 [Range] 等)
        if (!ModelState.IsValid)
        {
            // 提取第一个错误信息返回
            var errorMsg = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage ?? "参数校验失败";
            return BadRequest(ApiResponse<string>.Fail(400, errorMsg));
        }

        try
        {
            var dim = request.Specifications.Dimensions;
            _logger.LogInformation("开始生成 PSD 项目: {ProjectName}, 尺寸: {L}x{W}x{H}", 
                request.ProjectName, dim.Length, dim.Width, dim.Height);

            // 调用业务层
            var fileBytes = await _psdService.CreatePsdFileAsync(request);
                
            // 文件名处理：ProjectName + 时间戳
            var safeProjectName = string.Join("_", request.ProjectName.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{safeProjectName}_{DateTime.Now:yyyyMMddHHmm}.psd";

            // 返回文件流
            return File(fileBytes, "application/x-photoshop", fileName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "生成 PSD 参数异常");
            return BadRequest(ApiResponse<string>.Fail(400, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成 PSD 发生未捕获异常");
            return StatusCode(500, ApiResponse<string>.Fail(500, "生成文件时发生内部错误"));
        }
    }
}