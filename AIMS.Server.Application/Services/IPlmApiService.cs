using AIMS.Server.Application.DTOs.Plm;

namespace AIMS.Server.Application.Services;

public interface IPlmApiService
{
    // 直接返回业务需要的品牌列表，把 PLM 的外壳剥离逻辑留在 Service 内部
    Task<List<BrandDto>> GetBrandListAsync();
    /// <summary>
    /// 获取产品条码
    /// </summary>
    /// <param name="code">SKU编码</param>
    /// <returns>条码数据 (String)</returns>
    Task<BarCodeDto> GetBarCodeAsync(string code);
}