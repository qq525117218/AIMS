using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AIMS.Server.Application.DTOs.Plm;

public class PlmCodeRequestDto
{
    /// <summary>
    /// SKU 编码 (例如: SKU00001636)
    /// </summary>
    [Required(ErrorMessage = "SKU Code 不能为空")]
    [JsonPropertyName("code")] // 确保前端传 code 或 Code 都能接收
    public string Code { get; set; } = string.Empty;
}