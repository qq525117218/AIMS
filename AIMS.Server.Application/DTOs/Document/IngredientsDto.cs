namespace AIMS.Server.Application.DTOs.Document;

/// <summary>
/// 成分结构体
/// </summary>
public class IngredientsDto
{
    public string ActiveIngredients { get; set; } = string.Empty;
    public string InactiveIngredients { get; set; } = string.Empty;
    
    /// <summary>
    /// 原始文本 (如果正则拆分失败，则保留此值)
    /// </summary>
    public string RawText { get; set; } = string.Empty;
}