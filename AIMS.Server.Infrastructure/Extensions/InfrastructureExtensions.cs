using System.Reflection;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
// 引入必要的命名空间
using Aspose.PSD; 
// 注意：Aspose.Words 和 Aspose.Pdf 的 License 类名都是 License，
// 为避免冲突，下面代码中使用全限定名 (Aspose.Words.License)

namespace AIMS.Server.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    /// <summary>
    /// 注册基础设施层的服务（包括 Aspose License 初始化）
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // 1. 注册 Aspose 服务实现
        services.AddScoped<IPsdGenerator, AsposePsdGenerator>();
        services.AddScoped<IWordParser, AsposeWordParser>();
        services.AddScoped<IWordService, WordService>();

        // 2. 初始化 License (立即执行)
        InitAsposeLicense();

        return services;
    }

    private static void InitAsposeLicense()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            
            // 资源名称规则：默认命名空间.文件夹名.文件名
            // 请确保 namespace 和文件夹名字准确
            var resourceName = "AIMS.Server.Infrastructure.Licenses.Aspose.Total.NET.lic";

            // 1. 获取 License 资源流
            Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);

            // 如果找不到，尝试模糊搜索
            if (resourceStream == null)
            {
                var allResources = assembly.GetManifestResourceNames();
                var foundName = allResources.FirstOrDefault(x => x.EndsWith("Aspose.Total.NET.lic"));
                if (foundName != null)
                {
                    resourceName = foundName;
                    resourceStream = assembly.GetManifestResourceStream(foundName);
                    Console.WriteLine($"[System] 自动定位到 License 资源: {resourceName}");
                }
            }

            if (resourceStream == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("==================================================================");
                Console.WriteLine("[Warning] 未找到 Aspose License 嵌入资源！");
                Console.WriteLine("          Aspose 所有组件将以【评估模式】运行 (会有水印/红色文字)。");
                Console.WriteLine("==================================================================");
                Console.ResetColor();
                return;
            }

            // 2. 关键步骤：将资源流复制到 MemoryStream
            // 这样可以重复读取同一个流给不同的组件使用 (PSD, Words, PDF...)
            using (var ms = new MemoryStream())
            {
                resourceStream.CopyTo(ms);
                resourceStream.Dispose(); // 复制完成后释放原始资源流

                Console.WriteLine("----------- Aspose License Status -----------");

                // --- 初始化 Aspose.PSD ---
                try
                {
                    ms.Position = 0; // 重置流位置
                    var psdLic = new Aspose.PSD.License();
                    psdLic.SetLicense(ms);
                    Console.WriteLine(" [PSD]   License: ✅ Success");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($" [PSD]   License: ❌ Failed ({ex.Message})");
                    Console.ResetColor();
                }

                // --- 初始化 Aspose.Words (解决 Word 文档红色文字问题) ---
                try
                {
                    ms.Position = 0; // 重置流位置
                    var wordsLic = new Aspose.Words.License();
                    wordsLic.SetLicense(ms);
                    Console.WriteLine(" [Words] License: ✅ Success");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($" [Words] License: ❌ Failed ({ex.Message})");
                    Console.ResetColor();
                }

                // --- 初始化 Aspose.PDF (解决之前的条形码 PDF 处理问题) ---
                try
                {
                    // 如果项目没引用 Aspose.PDF，请注释掉这一块
                    ms.Position = 0; // 重置流位置
                    var pdfLic = new Aspose.Pdf.License();
                    pdfLic.SetLicense(ms);
                    Console.WriteLine(" [PDF]   License: ✅ Success");
                }
                catch (Exception ex)
                {
                    // 仅显示警告，不影响主程序
                    Console.WriteLine($" [PDF]   License: ⚠️ Skipped or Failed ({ex.Message})");
                }
                
                Console.WriteLine("---------------------------------------------");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Fatal Error] Aspose License 初始化过程发生严重异常: {ex.Message}");
            Console.ResetColor();
        }
    }
}