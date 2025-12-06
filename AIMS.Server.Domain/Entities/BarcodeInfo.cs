namespace AIMS.Server.Domain.Entities;

public class BarcodeInfo
{
    public string Value { get; set; } = string.Empty;
    public string Type { get; set; } = "EAN-13";
    
    public string Url { get; set; } = string.Empty;
}