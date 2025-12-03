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
    /// 生成的文件为 RGB 模式，300 DPI。包含出血线和安全区标记。
    /// </remarks>
    [HttpPost("generate/psd")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GeneratePsd([FromBody] PsdRequestDto request)
    {
        // 1. 基础参数校验 (由 [ApiController] 自动处理，但手动写出逻辑更健壮)
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<string>.Fail(400, "参数校验失败"));
        }

        try
        {
            _logger.LogInformation("开始生成 PSD: L={L}, H={H}, W={W}", request.Length, request.Height, request.Width);

            // 2. 调用业务层
            var fileBytes = await _psdService.CreatePsdFileAsync(request);
                
            // 3. 生成文件名
            var fileName = $"Template_L{request.Length}_H{request.Height}_{DateTime.Now:yyyyMMddHHmm}.psd";

            // 4. 返回文件流
            return File(fileBytes, "application/x-photoshop", fileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<string>.Fail(400, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成 PSD 失败");
            return StatusCode(500, ApiResponse<string>.Fail(500, "生成文件时发生内部错误"));
        }
    }
}