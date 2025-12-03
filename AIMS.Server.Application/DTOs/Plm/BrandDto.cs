using Newtonsoft.Json;

namespace AIMS.Server.Application.DTOs.Plm;

public class BrandDto
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; } = string.Empty;

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