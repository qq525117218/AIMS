using Newtonsoft.Json;

namespace AIMS.Server.Application.DTOs.Plm;

/// <summary>
/// 品牌详情 DTO
/// </summary>
public class BrandDetailDto
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("abbr")]
    public string? Abbr { get; set; }

    [JsonProperty("brand_category_names")]
    public string? BrandCategoryNames { get; set; }

    [JsonProperty("srm_company_supplier_id")]
    public int? SrmCompanySupplierId { get; set; }

    [JsonProperty("supplier_name")]
    public string? SupplierName { get; set; }

    [JsonProperty("logo")]
    public string? Logo { get; set; }

    [JsonProperty("status")]
    public int? Status { get; set; }

    [JsonProperty("defaultManufacturer")]
    public ManufacturerDto? DefaultManufacturer { get; set; }

    [JsonProperty("agent_ec")]
    public BrandAgentDto? AgentEc { get; set; }

    [JsonProperty("agent_uk")]
    public BrandAgentDto? AgentUk { get; set; }

    [JsonProperty("agent_us")]
    public BrandAgentDto? AgentUs { get; set; }

    [JsonProperty("agent_ca")]
    public BrandAgentDto? AgentCa { get; set; }
}

/// <summary>
/// 制造商信息
/// </summary>
public class ManufacturerDto
{
    [JsonProperty("manufacturer_name")]
    public string? ManufacturerName { get; set; }

    [JsonProperty("manufacturer_english_name")]
    public string? ManufacturerEnglishName { get; set; }

    [JsonProperty("manufacturer_address")]
    public string? ManufacturerAddress { get; set; }

    [JsonProperty("manufacturer_english_address")]
    public string? ManufacturerEnglishAddress { get; set; }

    [JsonProperty("country_region_name")]
    public string? CountryRegionName { get; set; }

    [JsonProperty("contact_name")]
    public string? ContactName { get; set; }

    [JsonProperty("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonProperty("mailbox")]
    public string? Mailbox { get; set; }
}

/// <summary>
/// 代理商信息 (EC/UK/US/CA 通用)
/// </summary>
public class BrandAgentDto
{
    [JsonProperty("agent_name")]
    public string? AgentName { get; set; }

    [JsonProperty("agent_type_name")]
    public string? AgentTypeName { get; set; }

    [JsonProperty("agent_address")]
    public string? AgentAddress { get; set; }

    [JsonProperty("contact_name")]
    public string? ContactName { get; set; }

    [JsonProperty("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonProperty("mailbox")]
    public string? Mailbox { get; set; }

    [JsonProperty("postal_code")]
    public string? PostalCode { get; set; }

    [JsonProperty("province")]
    public string? Province { get; set; }

    [JsonProperty("city")]
    public string? City { get; set; }
}