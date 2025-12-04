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
    private const string BaseUrl = "http://localhost:5000"; 

    public DocumentApiTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Call_Word_Parse_Api_Should_Return_Success()
    {
        var filePath = @"C:\Users\zob\Desktop\【标注】20251126-LANISKA-关节舒缓霜-产品文案.docx";

        if (!File.Exists(filePath))
        {
            _output.WriteLine($"[跳过] 本地文件不存在: {filePath}");
            return;
        }

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var base64String = Convert.ToBase64String(fileBytes);

        var requestDto = new WordParseRequestDto
        {
            FileName = Path.GetFileName(filePath),
            FileContentBase64 = base64String
        };

        using var client = new HttpClient();
        client.BaseAddress = new Uri(BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30); 

        _output.WriteLine($"正在发送请求到: {BaseUrl}/api/document/parse/word ...");

        try
        {
            // ✅ 核心修复：定义序列化选项，强制客户端发送 snake_case 格式
            var requestJsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, // 关键：转为下划线小写
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            };

            // ✅ 将 options 传入 PostAsJsonAsync 的第三个参数
            var response = await client.PostAsJsonAsync(
                "/api/document/parse/word", 
                requestDto, 
                requestJsonOptions 
            );

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _output.WriteLine($"请求失败! 状态码: {response.StatusCode}");
                _output.WriteLine($"错误详情: {errorContent}");
                Assert.Fail($"API 调用失败: {response.StatusCode}");
            }

            var resultJsonString = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resultJsonString);
            var root = doc.RootElement;

            // 验证字段 (服务端返回的也是 snake_case)
            if (root.TryGetProperty("is_success", out var successProp))
            {
                Assert.True(successProp.GetBoolean());
            }

            // 保存结果
            var prettyOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) 
            };
            var formattedJson = JsonSerializer.Serialize(root, prettyOptions);
            var outputFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                $"WordParseResult_{DateTime.Now:HHmmss}.json"
            );
            await File.WriteAllTextAsync(outputFilePath, formattedJson);
            
            _output.WriteLine("✅ 测试通过！");
            _output.WriteLine($"📄 结果已保存: {outputFilePath}");
            
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"异常: {ex.Message}");
            throw;
        }
    }
}