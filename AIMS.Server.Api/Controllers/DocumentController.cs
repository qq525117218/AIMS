using AIMS.Server.Application.DTOs.Document;
using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DocumentController : ControllerBase
{
    private readonly IWordService _wordService;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(IWordService wordService, ILogger<DocumentController> logger)
    {
        _wordService = wordService;
        _logger = logger;
    }

    /// <summary>
    /// 解析 Word 文档内容
    /// </summary>
    /// <remarks>
    /// 接受包含文件 Base64 编码的 JSON 请求，返回解析后的文本和元数据。
    /// </remarks>
    [HttpPost("parse/word")]
    [ProducesResponseType(typeof(ApiResponse<WordParseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ParseWord([FromBody] WordParseRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ApiResponse<string>.Fail(400, "无效的请求参数"));
        }

        try
        {
            _logger.LogInformation("开始解析文档: {FileName}", request.FileName);

            var result = await _wordService.ParseWordDocumentAsync(request);

            return Ok(ApiResponse<WordParseResponseDto>.Success(result));
        }
        catch (FormatException)
        {
            return BadRequest(ApiResponse<string>.Fail(400, "文件内容 Base64 格式错误"));
        }
        catch (InvalidOperationException ex) // 捕获领域/基础设施层抛出的已知错误
        {
            _logger.LogWarning(ex, "文档解析业务异常");
            return BadRequest(ApiResponse<string>.Fail(400, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文档解析发生未知错误");
            return StatusCode(500, ApiResponse<string>.Fail(500, "服务器内部错误"));
        }
    }
}