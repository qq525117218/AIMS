namespace AIMS.Server.Application.DTOs.Plm;

public class PlmBrandListResponse
{
    // 根据第三方实际返回字段定义
    public int Code { get; set; }
    public string Message { get; set; }
    public List<BrandItemDto> Data { get; set; }
}