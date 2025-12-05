using AIMS.Server.Domain.Entities;

namespace AIMS.Server.Domain.Interfaces;

/// <summary>
/// Word 文档解析器接口
/// </summary>
public interface IWordParser
{
    /// <summary>
    /// 解析 Word 文档流，提取文本和元数据
    /// </summary>
    /// <param name="fileStream">文件流</param>
    /// <returns>包含解析内容的模型</returns>
    Task<WordParseResult> ParseAsync(Stream fileStream);
}