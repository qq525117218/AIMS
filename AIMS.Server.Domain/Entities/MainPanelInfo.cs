namespace AIMS.Server.Domain.Entities;

public class MainPanelInfo
{
    public string BrandName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string CapacityInfo { get; set; } = string.Empty;
    public List<string> SellingPoints { get; set; } = new();
}