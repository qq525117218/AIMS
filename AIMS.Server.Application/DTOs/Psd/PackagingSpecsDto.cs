namespace AIMS.Server.Application.DTOs.Psd;

public class PackagingSpecsDto
{
    public DimensionsDto Dimensions { get; set; } = new();
    public PrintConfigDto PrintConfig { get; set; } = new();
}