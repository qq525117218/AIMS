using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.DTOs.Plm; // ApiResponse
using AIMS.Server.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ApiExplorerSettings(GroupName = "plm")]
public class PlmController : ControllerBase
{
    private readonly IPlmApiService _plmApiService;
    // 使用当前控制器的 Logger
    private readonly ILogger<PlmController> _logger;

    public PlmController(IPlmApiService plmApiService, ILogger<PlmController> logger)
    {
        _plmApiService = plmApiService;
        _logger = logger;
    }

    [HttpGet("brand/list")]
    // ✅ 返回类型改为 ApiResponse<object>，极大地增加了灵活性
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<object>> GetBrandList()
    {
        // 1. 获取纯净数据
        var brandList = await _plmApiService.GetBrandListAsync();

    
        if (brandList != null && brandList.Count > 0)
        {
            brandList = brandList.OrderBy(b => b.Code).ToList();
        }

        // 3. ✅ 使用匿名对象构建特定的前端结构
        var data = new 
        { 
            plm_brand_data = brandList 
        };

        // 4. 返回
        return ApiResponse<object>.Success(data);
    }
    
    [HttpPost("product/barcode")]
    [ProducesResponseType(typeof(ApiResponse<BarCodeDto>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<object>> GetBarCode([FromBody] PlmCodeRequestDto request)
    {
        // 1. 调用 Service 获取对象
        var barCodeDto = await _plmApiService.GetBarCodeAsync(request.Code);

        // 2. 直接返回对象
        // 全局 JSON 配置会将 BarCode -> bar_code, BarCodePath -> bar_code_path
        return ApiResponse<object>.Success(barCodeDto);
    }
    
    [HttpPost("brand/detail")]
    [ProducesResponseType(typeof(ApiResponse<BrandDetailDto>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<object>> GetBrandDetail([FromBody] PlmCodeRequestDto request)
    {
        // 1. 调用 Service 获取品牌详情对象
        // 注意：这里复用了 PlmCodeRequestDto，因为入参结构也是 { "code": "..." }
        var brandDetail = await _plmApiService.GetBrandDetailAsync(request.Code);

        // 2. 直接返回对象，ApiResponse<object>.Success 会自动包裹标准响应结构
        // 最终返回给前端的 JSON 结构中，data 字段即为 BrandDetailDto 的内容
        return ApiResponse<object>.Success(brandDetail);
    }
}