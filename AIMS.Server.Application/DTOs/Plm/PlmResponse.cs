using Newtonsoft.Json;

namespace AIMS.Server.Application.DTOs.Plm;

/// <summary>
/// PLM 接口通用响应结构
/// </summary>
/// <typeparam name="T">具体的 Data 数据类型</typeparam>
public class PlmResponse<T>
{
    [JsonProperty("code")]
    public int Code { get; set; }

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("request_id")]
    public string? RequestId { get; set; }

    [JsonProperty("data")]
    public T? Data { get; set; }
}