using Newtonsoft.Json;

namespace AIMS.Server.Application.DTOs.Plm;

public class BarCodeDto
{
    // Newtonsoft.Json 用于反序列化 PLM 的响应
    [JsonProperty("bar_code")]
    public string BarCode { get; set; } = string.Empty;

    [JsonProperty("bar_code_path")]
    public string BarCodePath { get; set; } = string.Empty;
}