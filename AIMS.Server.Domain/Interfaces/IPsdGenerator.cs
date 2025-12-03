using AIMS.Server.Domain.Entities;

namespace AIMS.Server.Domain.Interfaces;

public interface IPsdGenerator
{
    /// <summary>
    /// 生成 PSD 文件并返回字节流
    /// </summary>
    Task<byte[]> GeneratePsdAsync(PackagingDimensions dimensions);
}