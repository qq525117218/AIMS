using System.Reflection;
using AIMS.Server.Application.Services;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AIMS.Server.Infrastructure.Extensions;

 public static class InfrastructureExtensions
{
    /// <summary>
    /// 注册基础设施层的服务（包括 Aspose License 初始化）
    /// </summary>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // 1. 注册 Aspose 服务实现
        // 注意：这里假设你已经在 Infrastructure 层实现了 AsposePsdGenerator
        // 如果还没有实现，请先创建该类，或者暂时注释掉下面这行
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

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    // 尝试模糊搜索，防止命名空间不匹配导致找不到
                    var allResources = assembly.GetManifestResourceNames();
                    resourceName = allResources.FirstOrDefault(x => x.EndsWith("Aspose.Total.NET.lic"));
                    
                    if (resourceName != null)
                    {
                        using var stream2 = assembly.GetManifestResourceStream(resourceName);
                        var license = new Aspose.PSD.License();
                        license.SetLicense(stream2);
                        Console.WriteLine($"[System] Aspose License loaded from: {resourceName}");
                        return;
                    }

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[Warning] 未找到 Aspose License 嵌入资源，将使用评估模式运行。");
                    Console.ResetColor();
                    return;
                }

                var lic = new Aspose.PSD.License();
                lic.SetLicense(stream);
                Console.WriteLine("[System] Aspose License set successfully.");
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Aspose License 初始化失败: {ex.Message}");
            Console.ResetColor();
        }
    }
}