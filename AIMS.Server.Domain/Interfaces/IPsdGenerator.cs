using AIMS.Server.Domain.Entities;

namespace AIMS.Server.Domain.Interfaces;

public interface IPsdGenerator
{
    /// <summary>
    /// 生成 PSD 文件
    /// </summary>
    /// <param name="dimensions">物理规格 (刀版尺寸)</param>
    /// <param name="assets">视觉素材 (文案、条码) - 暂未启用</param>
    /// <returns>PSD 文件字节流</returns>
    Task<byte[]> GeneratePsdAsync(PackagingDimensions dimensions, PackagingAssets assets, Action<int, string>? onProgress = null);
}