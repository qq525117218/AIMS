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

    public async Task<PlmBrandListResponse> GetBrandListAsync()
    {
        // 1. 构建业务参数 (Payload)
        // 对应请求 JSON: { "is_include_delete": true }
        var payload = new { is_include_delete = true };

        // 2. 生成签名参数 (Query Params)
        var queryParam = GenSign(payload);

        string responseString = string.Empty;
        try
        {
            // 3. 发起请求
            var url = _options.BaseUrl.AppendPathSegment("/Brand/GetBrandList");
            
            _logger.LogInformation("Calling PLM API: {Url}", url);

            var response = await url
                .SetQueryParams(queryParam)
                .WithTimeout(TimeSpan.FromSeconds(15))
                .PostJsonAsync(payload);

            responseString = await response.GetStringAsync();

            // 4. 反序列化
            var result = JsonConvert.DeserializeObject<PlmBrandListResponse>(responseString);
            return result;
        }
        catch (FlurlHttpException ex)
        {
            // 处理 HTTP 错误 (4xx, 5xx)
            var errorBody = await ex.GetResponseStringAsync();
            _logger.LogError(ex, "PLM API Request Failed. Status: {Status}, Url: {Url}, Body: {Body}", 
                ex.Call?.Response?.StatusCode, ex.Call?.Request?.Url, errorBody);
            
            throw new Exception($"PLM 接口调用失败: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            // 处理其他错误 (网络不通、序列化失败等)
            _logger.LogError(ex, "Unexpected error calling PLM API. Response: {Response}", responseString);
            throw;
        }
    }

    /// <summary>
    /// 生成签名参数
    /// </summary>
    private PlmBaseQueryParam GenSign<T>(T signData) where T : class
    {
        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        
        // 假设 SignUtil 是你现有的工具类
        // 如果 SignUtil.GenSign 需要字典，这里可能需要将 signData 转为字典
        var signature = WestmoonSignUtil.GenSign(signData, timestamp, _options.AppSecret);

        return new PlmBaseQueryParam 
        { 
            app_key = _options.AppKey, 
            timestamp = timestamp, 
            signature = signature 
        };
    }
}