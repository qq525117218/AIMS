using System.Net.Http.Json;
using System.Text.Encodings.Web; 
using System.Text.Json;
using System.Text.Unicode;
using AIMS.Server.Application.DTOs.Document;

using Xunit;
using Xunit.Abstractions;

namespace AIMS.Server.Tests;

public class DocumentApiTest
{
    private readonly ITestOutputHelper _output;
    
    // ⚠️ 注意：请确保这里是你本地 API 启动的实际地址
    private const string BaseUrl = "http://localhost:5000"; 

    public DocumentApiTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Call_Word_Parse_Api_Should_Return_Success()
    {
        // 1. 准备本地文件路径
        var filePath = @"C:\Users\zob\Desktop\【标注】20251126-LANISKA-关节舒缓霜-产品文案.docx";

        if (!File.Exists(filePath))
        {
            _output.WriteLine($"[跳过] 本地文件不存在: {filePath}");
            return;
        }

        // 2. 读取文件并转 Base64
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var base64String = Convert.ToBase64String(fileBytes);

        var requestDto = new WordParseRequestDto
        {
            FileName = Path.GetFileName(filePath),
            FileContentBase64 = base64String
        };

        // 3. 创建 HttpClient 并发送请求
        using var client = new HttpClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30); 

        _output.WriteLine($"正在发送请求到: {BaseUrl}/api/Document/parse/word ...");

        try
        {
            var response = await client.PostAsJsonAsync("/api/Document/parse/word", requestDto);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _output.WriteLine($"请求失败! 状态码: {response.StatusCode}");
                _output.WriteLine($"错误详情: {errorContent}");
                Assert.Fail($"API 调用失败: {response.StatusCode}");
            }

            var resultJsonString = await response.Content.ReadAsStringAsync();
            
            // 4. 解析 JSON
            using var doc = JsonDocument.Parse(resultJsonString);
            var root = doc.RootElement;

            // 配置 JSON 序列化选项（支持中文不转义）
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) 
            };
            
            var formattedJson = JsonSerializer.Serialize(root, options);

            // ================================================================
            // ✅ 解决方案：将完整 JSON 输出到桌面文件，避免控制台截断
            // ================================================================
            var outputFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                $"WordParseResult_{DateTime.Now:HHmmss}.json"
            );
            
            await File.WriteAllTextAsync(outputFilePath, formattedJson);
            
            _output.WriteLine("✅ 测试通过！");
            _output.WriteLine($"📄 完整 JSON 结果已保存到文件: {outputFilePath}");
            _output.WriteLine("-----------------------------------------------------");

            // 5. 在控制台只打印关键摘要（避免截断）
            if (root.TryGetProperty("data", out var dataProp))
            {
                // 打印表格数量
                if (dataProp.TryGetProperty("extractedTables", out var tablesProp))
                {
                     _output.WriteLine($"📊 提取到的表格数量: {tablesProp.GetArrayLength()}");
                }
                
                // 打印前 500 个字符预览
                if (dataProp.TryGetProperty("fullTextPreview", out var textProp))
                {
                    var text = textProp.GetString() ?? "";
                    var preview = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
                    _output.WriteLine($"📝 文本内容预览:\n{preview}");
                }
            }
            
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _output.WriteLine($"无法连接到服务器: {ex.Message}");
            _output.WriteLine($"请检查 BaseUrl 是否正确: {BaseUrl}");
            throw;
        }
    }
}