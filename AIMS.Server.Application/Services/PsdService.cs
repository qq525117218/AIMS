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

    public async Task<byte[]> CreatePsdFileAsync(PsdRequestDto dto)
    {
        // 1. DTO 转 Domain Entity (在这里进行业务规则校验)
        var dimensions = new PackagingDimensions(
            dto.Length, 
            dto.Height, 
            dto.Width, 
            dto.BleedLeftRight, 
            dto.BleedTopBottom, 
            dto.InnerBleed
        );

        // 2. 调用基础设施层生成文件
        return await _psdGenerator.GeneratePsdAsync(dimensions);
    }
}