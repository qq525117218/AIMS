using Newtonsoft.Json;

namespace AIMS.Server.Application.DTOs.Plm;

/// <summary>
/// PLM 系统返回的品牌实体
/// </summary>
public class BrandDto
{
    // 对应 PLM JSON 中的 "id"
    [JsonProperty("id")]
    public int Id { get; set; }

    // 对应 PLM JSON 中的 "code"
    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

    // 对应 PLM JSON 中的 "name"
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("abbr")]
    public string Abbr { get; set; } = string.Empty;

    [JsonProperty("brand_category_name")]
    public string BrandCategoryName { get; set; } = string.Empty;

    [JsonProperty("departmentname")]
    public string DepartmentName { get; set; } = string.Empty;

    [JsonProperty("status")]
    public int Status { get; set; }

    [JsonProperty("is_deleted")]
    public int IsDeleted { get; set; }
}