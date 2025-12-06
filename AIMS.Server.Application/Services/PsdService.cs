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
        // 1. 转换规格 (Dimensions) DTO -> Domain Entity
        var spec = dto.Specifications;
        var dimensions = new PackagingDimensions(
            spec.Dimensions.Length,
            spec.Dimensions.Height,
            spec.Dimensions.Width,
            spec.PrintConfig.BleedX,
            spec.PrintConfig.BleedY,
            spec.PrintConfig.BleedInner
        );

        // 2. 转换素材 (Assets) DTO -> Domain Entity
        // 这里我们手动创建一个新的 Domain 对象，将 DTO 的值赋给它
        var assets = new PackagingAssets
        {
            Texts = new TextAssets
            {
                MainPanel = new MainPanelInfo 
                {
                    BrandName = dto.Assets.Texts.MainPanel.BrandName,
                    ProductName = dto.Assets.Texts.MainPanel.ProductName,
                    CapacityInfo = dto.Assets.Texts.MainPanel.CapacityInfo,
                    SellingPoints = dto.Assets.Texts.MainPanel.SellingPoints
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
                    Type = dto.Assets.DynamicImages.Barcode.Type
                }
            }
        };

        // 3. 调用生成器
        // ❌ 错误写法：return await _psdGenerator.GeneratePsdAsync(dimensions, dto.Assets); 
        // ✅ 正确写法：传入上面创建的 assets 变量
        return await _psdGenerator.GeneratePsdAsync(dimensions, assets, onProgress);
    }
}