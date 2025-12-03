using System.Text.RegularExpressions;
using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using Aspose.Words;
using Aspose.Words.Tables;

namespace AIMS.Server.Infrastructure.Services;

public class AsposeWordParser : IWordParser
{
    public Task<WordParseResult> ParseAsync(Stream fileStream)
    {
        return Task.Run(() =>
        {
            var result = new WordParseResult();

            try
            {
                var doc = new Document(fileStream);

                result.PageCount = doc.PageCount;
                result.FullText = doc.ToString(SaveFormat.Text).Trim();

                // 提取段落
                var paragraphs = doc.GetChildNodes(NodeType.Paragraph, true);
                foreach (Paragraph para in paragraphs)
                {
                    var text = para.ToString(SaveFormat.Text).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Paragraphs.Add(text);
                    }
                }

                // 提取表格数据
                var tables = doc.GetChildNodes(NodeType.Table, true);

                foreach (Table table in tables)
                {
                    var currentTableData = new List<List<string>>();

                    foreach (Row row in table.Rows)
                    {
                        var rowData = new List<string>();

                        foreach (Cell cell in row.Cells)
                        {
                            string rawText = cell.ToString(SaveFormat.Text);
                            string cleanedText = SanitizeText(rawText);
                            rowData.Add(cleanedText);
                        }
                        currentTableData.Add(rowData);
                    }

                    if (currentTableData.Count > 0)
                    {
                        result.Tables.Add(currentTableData);
                    }
                }

                if (doc.BuiltInDocumentProperties != null)
                {
                    result.Metadata["Author"] = doc.BuiltInDocumentProperties.Author;
                    result.Metadata["Title"] = doc.BuiltInDocumentProperties.Title;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AsposeWordParser] Error: {ex.Message}");
                throw new InvalidOperationException("Word 解析失败", ex);
            }

            return result;
        });
    }

    /// <summary>
    /// 基础文本清洗：去除物理层面的脏字符
    /// </summary>
    private string SanitizeText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // 1. 去除 Aspose 特有的单元格结束符
        var text = input.Replace("\a", "");

        // 2. 统一替换换行、制表符、特殊空格为普通空格
        text = text.Replace("\r", " ")
                   .Replace("\n", " ")
                   .Replace("\t", " ")
                   .Replace("\v", " ")
                   .Replace("\f", " ")
                   .Replace(ControlChar.NonBreakingSpace, " ")
                   .Replace("\u00A0", " ");

        // 3. 关键：将 + 替换为空格，彻底解决 JSON 序列化 \u002B 问题
        // 比如 "品牌+产品名" -> "品牌 产品名"
        text = text.Replace("+", " ");

        // 4. 正则合并：将连续的多个空格合并为一个
        text = Regex.Replace(text, @"\s+", " ");

        return text.Trim();
    }
}