using Newtonsoft.Json;

namespace AIMS.Server.Application.DTOs.Plm;

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

    // 这是一个泛型，可以适应 BrandDto, ProductDto 等各种列表或对象
    [JsonProperty("data")]
    public T? Data { get; set; }
}