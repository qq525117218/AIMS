namespace AIMS.Server.Domain.Entities;

public class MainPanelInfo
{
    public string BrandName { get; set; } = string.Empty;
    public string CapacityInfo { get; set; } = string.Empty;
    public string CapacityInfoBack { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<string> SellingPoints { get; set; } = new();
}