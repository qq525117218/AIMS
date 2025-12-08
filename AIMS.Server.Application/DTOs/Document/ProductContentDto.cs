namespace AIMS.Server.Application.DTOs.Document;

public class ProductContentDto
{
    /// <summary>
    /// 产品名称 (已去除 "PRODUCT NAME:" 前缀)
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// 成分 (已拆分为活性/非活性结构)
    /// </summary>
    public IngredientsDto Ingredients { get; set; } = new();

    /// <summary>
    /// 警告语 (已去除 "WARNINGS:" 前缀)
    /// </summary>
    public string Warnings { get; set; } = string.Empty;

    /// <summary>
    /// 保质期 (已去除 "SHELF LIFE:" 前缀)
    /// </summary>
    public string ShelfLife { get; set; } = string.Empty;

    /// <summary>
    /// 制造商 (已去除 "MANUFACTURER" 前缀)
    /// </summary>
    public string Manufacturer { get; set; } = string.Empty;

    /// <summary>
    /// 地址 (已去除 "ADDRESS" 前缀)
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 原产国 (已去除 "MADE IN" 前缀)
    /// </summary>
    public string CountryOfOrigin { get; set; } = string.Empty;

    /// <summary>
    /// 建议使用方法 (已去除 "DIRECTIONS:" 前缀)
    /// </summary>
    public string Directions { get; set; } = string.Empty;
    
    /// <summary>
    /// 产品优势 (已去除 "FUNCTIONS:" 前缀)
    /// </summary>
    public string Benefits { get; set; } = string.Empty;
}
