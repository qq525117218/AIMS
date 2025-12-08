namespace AIMS.Server.Application.DTOs.Psd;

public class MainPanelDto
{
    public string BrandName { get; set; } = string.Empty;
   
    public string CapacityInfo { get; set; } = string.Empty;
    
    // --- 新增字段开始 ---
    /// <summary>
    /// 背面显示的净含量 (对应 JSON: capacity_info_back)
    /// </summary>
    public string CapacityInfoBack { get; set; } = string.Empty;

    /// <summary>
    /// 正面显示的厂商名称 (对应 JSON: manufacturer)
    /// </summary>
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>
    /// 正面显示的地址 (对应 JSON: address)
    /// </summary>
    public string Address { get; set; } = string.Empty;
    // --- 新增字段结束 ---

    public List<string> SellingPoints { get; set; } = new();
}