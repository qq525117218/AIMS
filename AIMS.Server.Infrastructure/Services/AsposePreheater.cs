using System;
using System.Threading.Tasks;
using Aspose.Words;
using Aspose.PSD;
using Aspose.PSD.ImageOptions;

namespace AIMS.Server.Infrastructure.Services;

public static class AsposePreheater
{
    /// <summary>
    /// 执行 Aspose 核心库的预热（加载 DLL、校验 License、构建字体缓存）
    /// </summary>
    public static async Task PreloadAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                // 1. 预热 Aspose.Words
                // 创建一个空文档会触发 DLL 加载、License 检查和字体扫描
                var doc = new Document();
                doc.Range.Replace("warmup", "start"); // 触发排版引擎
                
                // 2. 预热 Aspose.PSD
                // 创建一个小画布触发图形引擎初始化
                using (var psd = new Aspose.PSD.FileFormats.Psd.PsdImage(1, 1))
                {
                    // 简单访问属性触发加载
                    var w = psd.Width;
                }
            }
            catch (Exception ex)
            {
                // 这里只打印控制台，异常抛出给上层记录日志
                Console.WriteLine($"[AsposePreheater] Warning: Preload failed. {ex.Message}");
                throw;
            }
        });
    }
}