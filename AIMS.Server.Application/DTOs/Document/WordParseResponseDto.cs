namespace AIMS.Server.Application.DTOs.Document;

public class WordParseResponseDto
{
    /// <summary>
    /// 文档元数据 (文件信息、页数、作者等)
    /// </summary>
    public DocumentMetadataDto Meta { get; set; } = new();

    /// <summary>
    /// 提取的核心业务内容 (强类型，字段固定，便于前端绑定)
    /// </summary>
    public ProductContentDto Content { get; set; } = new();

    /// <summary>
    /// 原始表格数据 (保留用于调试、展示或兜底)
    /// </summary>
    public List<List<List<string>>> RawTables { get; set; } = new();
}