namespace AIMS.Server.Application.DTOs.Document;

/// <summary>
/// 文档元数据
/// </summary>
public class DocumentMetadataDto
{
    public string FileName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}