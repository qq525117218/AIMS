using AIMS.Server.Application.DTOs.Plm;
using AIMS.Server.Application.Options;
using AIMS.Server.Application.Services;
using AIMS.Server.Infrastructure.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Flurl;
using Flurl.Http;
using Newtonsoft.Json;

namespace AIMS.Server.Infrastructure.Services;

public class PlmApiService : IPlmApiService
{
    private readonly PlmOptions _options;
    private readonly ILogger<PlmApiService> _logger;

    public PlmApiService(IOptions<PlmOptions> options, ILogger<PlmApiService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<BrandDto>> GetBrandListAsync()
    {
        var payload = new { is_include_delete = true };
        var queryParam = GenSign(payload);
        try
        {
            var url = _options.BaseUrl.AppendPathSegment("/Brand/GetBrandList");
            _logger.LogInformation("Calling PLM API: {Url}", url);
            var response = await url
                .SetQueryParams(queryParam)
                .WithTimeout(TimeSpan.FromSeconds(15))
                .PostJsonAsync(payload);

            var responseString = await response.GetStringAsync();

            // ✅ 核心重构：直接使用 PlmResponse<T> 泛型解析
            var plmResult = JsonConvert.DeserializeObject<PlmResponse<List<BrandDto>>>(responseString);
            // 健壮性检查
            if (plmResult == null) throw new Exception("PLM 响应为空");
            if (!plmResult.Success) throw new Exception($"PLM 业务异常: {plmResult.Message}");

            return plmResult.Data ?? new List<BrandDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PLM 接口调用失败");
            throw; // 抛出异常由全局过滤器处理
        }
    }
    
    
    public async Task<BarCodeDto> GetBarCodeAsync(string code)
    {
        var payload = new { code = code };
        var queryParam = GenSign(payload);

        try
        {
            var url = _options.BaseUrl.AppendPathSegment("/Product/GetBarCode");
            _logger.LogInformation("Calling PLM BarCode API: {Url}, Code: {Code}", url, code);

            var response = await url
                .SetQueryParams(queryParam)
                .WithTimeout(TimeSpan.FromSeconds(15))
                .PostJsonAsync(payload);

            var responseString = await response.GetStringAsync();

            // ✅ 核心修改：使用 BarCodeDto 进行泛型解析
            // 匹配结构: { "data": { "bar_code": "...", "bar_code_path": "..." } }
            var plmResult = JsonConvert.DeserializeObject<PlmResponse<BarCodeDto>>(responseString);

            if (plmResult == null) throw new Exception("PLM 响应为空");
            if (!plmResult.Success) throw new Exception($"PLM 业务异常: {plmResult.Message}");

            // 返回对象，如果为空则返回默认实例
            return plmResult.Data ?? new BarCodeDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取条码失败");
            throw;
        }
    }

    // 签名方法保持不变...
    private PlmBaseQueryParam GenSign<T>(T signData) where T : class
    {
        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        var signature = WestmoonSignUtil.GenSign(signData, timestamp, _options.AppSecret);
        return new PlmBaseQueryParam { app_key = _options.AppKey, timestamp = timestamp, signature = signature };
    }
}