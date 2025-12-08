using System.Text.RegularExpressions;
using AIMS.Server.Application.DTOs.Document;
using AIMS.Server.Domain.Interfaces;

namespace AIMS.Server.Application.Services;

public class WordService : IWordService
{
    private readonly IWordParser _wordParser;

    public WordService(IWordParser wordParser)
    {
        _wordParser = wordParser;
    }

    public async Task<WordParseResponseDto> ParseWordDocumentAsync(WordParseRequestDto request)
    {
        // 1. Base64 解码
        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(request.FileContentBase64);
        }
        catch (FormatException)
        {
            throw new ArgumentException("文件内容 Base64 格式无效");
        }

        using var memoryStream = new MemoryStream(fileBytes);

        // 2. 调用基础设施层通用解析 (获取干净的字符串数据)
        var parseResult = await _wordParser.ParseAsync(memoryStream);

        // 3. 组装元数据
        var metadata = new DocumentMetadataDto
        {
            FileName = request.FileName,
            PageCount = parseResult.PageCount,
            Author = parseResult.Metadata.GetValueOrDefault("Author", ""),
            Title = parseResult.Metadata.GetValueOrDefault("Title", "")
        };

        // 4. 执行业务提取与深度清洗
        var content = ExtractProductContent(parseResult.Tables);

        return new WordParseResponseDto
        {
            Meta = metadata,
            Content = content,
            RawTables = parseResult.Tables
        };
    }

    /// <summary>
    /// 核心业务：从表格中提取字段并应用特定清洗规则
    /// </summary>
    private ProductContentDto ExtractProductContent(List<List<List<string>>> tables)
    {
        var dto = new ProductContentDto();

        if (tables == null || tables.Count == 0) return dto;

        foreach (var table in tables)
        {
            foreach (var row in table)
            {
                // 健壮性：确保行至少有两列 (Key, Value)
                if (row == null || row.Count < 2) continue;

                var keyCol = row[0];   // 比如 "产品名称"
                var valueCol = row[1]; // 比如 "PRODUCT NAME: xxx"

                if (string.IsNullOrWhiteSpace(keyCol)) continue;

                // 使用 Contains 模糊匹配，忽略大小写
                if (keyCol.Contains("产品名称", StringComparison.OrdinalIgnoreCase))
                {
                    dto.ProductName = CleanProductName(valueCol);
                }
                else if (keyCol.Contains("成分活性", StringComparison.OrdinalIgnoreCase))
                {
                    // 复杂业务：拆分成分
                    dto.Ingredients = ParseIngredients(valueCol);
                }
                else if (keyCol.Contains("警告语", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Warnings = CleanPrefix(valueCol, "WARNINGS");
                }
                else if (keyCol.Contains("保质期", StringComparison.OrdinalIgnoreCase))
                {
                    dto.ShelfLife = CleanPrefix(valueCol, "SHELF LIFE");
                }
                else if (keyCol.Contains("制造商", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Manufacturer = CleanPrefix(valueCol, "MANUFACTURER");
                }
                else if (keyCol.Contains("地址", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Address = CleanPrefix(valueCol, "ADDRESS");
                }
                else if (keyCol.Contains("原产国", StringComparison.OrdinalIgnoreCase))
                {
                    dto.CountryOfOrigin = CleanPrefix(valueCol, "MADE IN");
                }
                else if (keyCol.Contains("建议使用方法", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Directions = CleanPrefix(valueCol, "DIRECTIONS");
                }
                else if (keyCol.Contains("产品优势", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Benefits = CleanPrefix(valueCol, "FUNCTIONS");
                }
            }
        }

        return dto;
    }

    // --- 👇 私有清洗方法 (业务规则封装) ---

    /// <summary>
    /// 清洗产品名称：去掉 "PRODUCT NAME:" 及其变体
    /// </summary>
    private string CleanProductName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        
        // 正则：匹配开头的 "PRODUCT NAME" 或 "产品名称"，后面跟可选冒号和空格
        return Regex.Replace(input, @"^(PRODUCT NAME|产品名称)[:：]?\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>
    /// 通用前缀清洗：去除指定的前缀单词
    /// </summary>
    private string CleanPrefix(string input, string prefix)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        return Regex.Replace(input, $@"^({prefix})[:：]?\s*", "", RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>
    /// 解析成分：将长文本拆分为 Active 和 Inactive
    /// </summary>
    private IngredientsDto ParseIngredients(string input)
    {
        var result = new IngredientsDto { RawText = input };
        if (string.IsNullOrWhiteSpace(input)) return result;

        // 1. 预处理：统一中英文冒号，确保正则匹配准确
        var normalized = input.Replace("：", ":");

        // 2. 正则模式
        // ACTIVE INGREDIENTS[...] (捕获内容) INACTIVE INGREDIENTS[...] (捕获内容)
        // 兼容英文和中文标识
        var pattern = @"ACTIVE INGREDIENTS[:\s]*(?<active>.*?)(INACTIVE INGREDIENTS|非活性成分)[:\s]*(?<inactive>.*)";
        
        var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (match.Success)
        {
            // 提取并去除末尾可能残留的标点符号
            result.ActiveIngredients = match.Groups["active"].Value.Trim().TrimEnd(',', '、', ';');
            result.InactiveIngredients = match.Groups["inactive"].Value.Trim();
        }
        else
        {
            // 降级处理：如果格式不标准，无法拆分，则把全文放到 RawText，Active 设为全文或空视业务决定
            // 这里保留 Active 为空，前端可以使用 RawText 兜底
        }

        return result;
    }
}