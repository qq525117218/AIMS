using AIMS.Server.Application.DTOs.Plm;

namespace AIMS.Server.Application.Services;

public interface IPlmApiService
{
    /// <summary>
    /// 获取 PLM 品牌列表
    /// </summary>
    Task<PlmBrandListResponse> GetBrandListAsync();
}