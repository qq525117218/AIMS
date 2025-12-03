using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Plm;
using AIMS.Server.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] // 保持需要登录
public class PlmController : ControllerBase
{
    private readonly IPlmApiService _plmApiService;
    private readonly ILogger<PlmController> _logger;

    public PlmController(IPlmApiService plmApiService, ILogger<PlmController> logger)
    {
        _plmApiService = plmApiService;
        _logger = logger;
    }

    /// <summary>
    /// 获取品牌列表 (对接第三方 PLM)
    /// </summary>
    /// <remarks>
    /// 调用第三方接口 /Brand/GetBrandList，获取包含已删除的品牌列表
    /// </remarks>
    [HttpGet("brand/list")]
    [ProducesResponseType(typeof(ApiResponse<PlmBrandListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetBrandList()
    {
        try
        {
            var result = await _plmApiService.GetBrandListAsync();
            return Ok(ApiResponse<PlmBrandListResponse>.Success(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取 PLM 品牌列表失败");
            return StatusCode(500, ApiResponse<string>.Fail(500, "调用第三方系统失败，请联系管理员"));
        }
    }
}