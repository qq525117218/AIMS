namespace AIMS.Server.Application.DTOs.Psd;

public class PackagingAssetsDto
{
    public TextAssetsDto Texts { get; set; } = new();
    public DynamicImagesDto DynamicImages { get; set; } = new();
}