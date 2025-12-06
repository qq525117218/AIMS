namespace AIMS.Server.Application.DTOs.Psd;

public class MainPanelDto
{
    public string BrandName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string CapacityInfo { get; set; } = string.Empty;
    public List<string> SellingPoints { get; set; } = new();
}