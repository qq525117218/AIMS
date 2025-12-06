namespace AIMS.Server.Domain.Entities;

/// <summary>
/// 包装素材聚合根 (文案、图片、条码)
/// </summary>
public class PackagingAssets
{
    public TextAssets Texts { get; set; } = new();
    public DynamicImages Images { get; set; } = new();
}