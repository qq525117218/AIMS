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
    
    /// <summary>
    /// 获取品牌详情
    /// </summary>
    /// <param name="code">品牌编码/ID (根据实际业务含义，此处对应第三方入参的code)</param>
    Task<BrandDetailDto> GetBrandDetailAsync(string code);
}