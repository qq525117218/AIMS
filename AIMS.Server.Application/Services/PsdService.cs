using AIMS.Server.Application.DTOs.Psd;
using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;

namespace AIMS.Server.Application.Services;

public class PsdService : IPsdService
{
    private readonly IPsdGenerator _psdGenerator;

    public PsdService(IPsdGenerator psdGenerator)
    {
        _psdGenerator = psdGenerator;
    }

    public async Task<byte[]> CreatePsdFileAsync(PsdRequestDto dto, Action<int, string>? onProgress = null)
    {
        // 1. 转换规格 (Dimensions)
        var spec = dto.Specifications;
        var dimensions = new PackagingDimensions(
            spec.Dimensions.Length,
            spec.Dimensions.Height,
            spec.Dimensions.Width,
            spec.PrintConfig.BleedX,
            spec.PrintConfig.BleedY,
            spec.PrintConfig.BleedInner
        );

        // 2. 转换素材 (Assets)
        var assets = new PackagingAssets
        {
            Texts = new TextAssets
            {
                MainPanel = new MainPanelInfo 
                {
                    BrandName = dto.Assets.Texts.MainPanel.BrandName,
                    ProductName = dto.Assets.Texts.MainPanel.ProductName,
                    CapacityInfo = dto.Assets.Texts.MainPanel.CapacityInfo,
                    // 确保映射了其他字段如 SellingPoints, Manufacturer, Address 等...
                    SellingPoints = dto.Assets.Texts.MainPanel.SellingPoints,
                    CapacityInfoBack = dto.Assets.Texts.MainPanel.CapacityInfoBack,
                    Manufacturer = dto.Assets.Texts.MainPanel.Manufacturer,
                    Address = dto.Assets.Texts.MainPanel.Address
                },
                InfoPanel = new InfoPanelInfo
                {
                    Ingredients = dto.Assets.Texts.InfoPanel.Ingredients,
                    Manufacturer = dto.Assets.Texts.InfoPanel.Manufacturer,
                    Origin = dto.Assets.Texts.InfoPanel.Origin,
                    Warnings = dto.Assets.Texts.InfoPanel.Warnings,
                    Directions = dto.Assets.Texts.InfoPanel.Directions,
                    Address = dto.Assets.Texts.InfoPanel.Address
                }
            },
            Images = new DynamicImages
            {
                Barcode = new BarcodeInfo
                {
                    Value = dto.Assets.DynamicImages.Barcode.Value,
                    Type = dto.Assets.DynamicImages.Barcode.Type,
                    // ✅ 新增：映射 Url
                    Url = dto.Assets.DynamicImages.Barcode.Url
                }
            }
        };

        // 3. 调用生成器
        return await _psdGenerator.GeneratePsdAsync(dimensions, assets, onProgress);
    }
}