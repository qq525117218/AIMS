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
    public async Task<BrandDetailDto> GetBrandDetailAsync(string code)
    {
        // 1. 构建请求载荷
        var payload = new { code = code };
        
        // 2. 生成签名
        var queryParam = GenSign(payload);

        try
        {
            // 3. 构建 URL
            var url = _options.BaseUrl.AppendPathSegment("/Brand/BrandDetail");
            _logger.LogInformation("Calling PLM BrandDetail API: {Url}, Code: {Code}", url, code);

            // 4. 发起 HTTP POST 请求
            var response = await url
                .SetQueryParams(queryParam)
                .WithTimeout(TimeSpan.FromSeconds(15))
                .PostJsonAsync(payload);

            var responseString = await response.GetStringAsync();

            // 5. 反序列化 (使用泛型 PlmResponse<BrandDetailDto>)
            var plmResult = JsonConvert.DeserializeObject<PlmResponse<BrandDetailDto>>(responseString);

            // 6. 校验结果
            if (plmResult == null) throw new Exception("PLM 响应为空");
            if (!plmResult.Success) throw new Exception($"PLM 业务异常: {plmResult.Message}");

            // 7. 返回数据 (如果为 null 则返回空对象，防止上层空引用，可根据业务需要调整)
            return plmResult.Data ?? new BrandDetailDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取品牌详情失败");
            throw; // 抛出异常由全局过滤器处理
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