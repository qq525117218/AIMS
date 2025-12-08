using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Utils;
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.FillLayers;
using Aspose.PSD.FileFormats.Psd.Layers.FillSettings;
using Aspose.PSD.FileFormats.Psd.Layers.SmartObjects;
using Aspose.PSD.ImageOptions;
using Aspose.PSD.ProgressManagement;

// 显式指定 Aspose 类型以解决与 System.Drawing 的冲突
using Color = Aspose.PSD.Color;
using PointF = Aspose.PSD.PointF;
using Rectangle = Aspose.PSD.Rectangle;
using Point = Aspose.PSD.Point;

// 引入 Aspose.Pdf 别名
using AsposePdf = Aspose.Pdf; 
using AsposePdfDevices = Aspose.Pdf.Devices;

namespace AIMS.Server.Infrastructure.Services;

public class AsposePsdGenerator : IPsdGenerator
{
    private const float DPI = 300f; 
    
    // --- 条形码尺寸 ---
    private const double BARCODE_WIDTH_CM = 10.0; 
    private const double BARCODE_HEIGHT_CM = 5.0; 

    // --- 固定图标尺寸 ---
    private const double ICON_WIDTH_CM = 10;
    private const double ICON_HEIGHT_CM = 5;

    // ✅ 新增：Logo 最大限制尺寸 (宽8cm, 高4cm，防止 Logo 过大)
    private const double LOGO_MAX_WIDTH_CM = 8.0;
    private const double LOGO_MAX_HEIGHT_CM = 4.0;

    private static readonly HttpClient _httpClient = new HttpClient();
    
    static AsposePsdGenerator()
    {
        try
        {
            var fontFolders = new List<string>();

            // 1. 添加自定义字体目录 (兼容 Linux/Docker 和 Windows 开发环境)
            string customFontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts");
            if (Directory.Exists(customFontsDir))
            {
                fontFolders.Add(customFontsDir);
            }

            // 2. 修正：如果是 Windows，必须显式把系统字体目录加回来
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                fontFolders.Add(@"C:\Windows\Fonts"); 
            }
            else
            {
                // Linux 下常见的字体路径
                fontFolders.Add("/usr/share/fonts");
                fontFolders.Add("/usr/local/share/fonts");
            }

            // 3. 应用字体设置
            if (fontFolders.Count > 0)
            {
                FontSettings.SetFontsFolders(fontFolders.ToArray(), true);
                Console.WriteLine($"[AsposePsdGenerator] 已重置字体目录: {string.Join(", ", fontFolders)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AsposePsdGenerator] 字体配置警告: {ex.Message}");
        }
    }

    public async Task<byte[]> GeneratePsdAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress = null)
    {
        return await Task.Run(async () => await GenerateInternalAsync(dim, assets, onProgress));
    }

    private async Task<byte[]> GenerateInternalAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress)
    {
        string? tempPsdPath = null;
        string? tempPdfPath = null;
        
        string fixedIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Templates", "FixedIcon.psd");

        try
        {
            // --- 阶段 1: 初始化与基础绘制 ---
            onProgress?.Invoke(1, "正在初始化画布参数...");

            var X = CmToPixels(dim.Length); 
            var Y = CmToPixels(dim.Height); 
            var Z = CmToPixels(dim.Width);  

            var A = CmToPixels(dim.BleedLeftRight); 
            var B = CmToPixels(dim.BleedTopBottom); 
            var C = CmToPixels(dim.InnerBleed);     

            var totalWidth = (2 * X) + (2 * Z) + (2 * A);
            var calculatedHeight = Y + (2 * Z) + (2 * B) - (4 * C);
            var minRequiredHeight = B + (2 * Z) + Y;
            var totalHeight = Math.Max(calculatedHeight, minRequiredHeight);

            tempPsdPath = Path.GetTempFileName(); 

            // --- 阶段 2: 生成基础 PSD ---
            onProgress?.Invoke(5, "正在创建基础图层...");
            
            using (var psdImage = new PsdImage(totalWidth, totalHeight))
            {
                psdImage.SetResolution(DPI, DPI);

                onProgress?.Invoke(10, "正在绘制刀版结构...");
                DrawStructureLayers(psdImage, X, Y, Z, A, B, C, onProgress);

                onProgress?.Invoke(40, "正在生成智能辅助线...");
                AddGuidelines(psdImage, X, Y, Z, A, B, C);

                onProgress?.Invoke(50, "正在渲染文本信息...");
                DrawInfoPanelAssets(psdImage, assets, dim);
                DrawMainPanelAssets(psdImage, assets, dim);

                onProgress?.Invoke(70, "正在保存中间状态...");
                var saveOptions = new PsdOptions
                {
                    CompressionMethod = CompressionMethod.RLE,
                    ColorMode = ColorModes.Rgb
                };
                psdImage.Save(tempPsdPath, saveOptions);
            }

            // --- 阶段 3: 处理条形码 ---
            if (assets.Images?.Barcode != null && !string.IsNullOrEmpty(assets.Images.Barcode.Url))
            {
                onProgress?.Invoke(72, "正在下载条形码...");
                
                var pdfBytes = await _httpClient.GetByteArrayAsync(assets.Images.Barcode.Url);
                if (pdfBytes.Length > 0)
                {
                    tempPdfPath = Path.GetTempFileName();
                    await File.WriteAllBytesAsync(tempPdfPath, pdfBytes);

                    onProgress?.Invoke(75, "正在处理条形码智能对象...");
                    EmbedBarcodePdfAsSmartObject(tempPsdPath, tempPdfPath, dim);
                }
            }

            // ✅ 阶段 3.5: 处理动态品牌 Logo (新增逻辑) ---
            var brandName = assets.Texts?.MainPanel?.BrandName;
            if (!string.IsNullOrWhiteSpace(brandName))
            {
                // 清洗文件名，防止非法字符
                var safeBrandName = string.Join("_", brandName.Split(Path.GetInvalidFileNameChars()));
                string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Logo", $"{safeBrandName}.psd");

                if (File.Exists(logoPath))
                {
                    onProgress?.Invoke(78, $"正在置入品牌 Logo: {safeBrandName}...");
                    // 调用专门的 Logo 置入方法
                    EmbedLogoAsSmartObject(tempPsdPath, logoPath, dim);
                }
                else
                {
                    Console.WriteLine($"[Warning] 未找到品牌 Logo 文件: {logoPath}，跳过置入。");
                }
            }

            // --- 阶段 4: 处理固定图标 ---
            if (File.Exists(fixedIconPath))
            {
                onProgress?.Invoke(82, "正在置入固定图标...");
                EmbedFixedAssetAsSmartObject(tempPsdPath, fixedIconPath, dim);
            }
            else
            {
                Console.WriteLine($"[Warning] 未找到固定图标文件: {fixedIconPath}");
            }

            // --- 阶段 5: 读取最终文件并返回 ---
            onProgress?.Invoke(90, "生成完成，准备输出...");
            
            if (!File.Exists(tempPsdPath))
                throw new FileNotFoundException("生成过程中文件丢失", tempPsdPath);

            return await File.ReadAllBytesAsync(tempPsdPath);
        }
        catch (Exception ex)
        {
            onProgress?.Invoke(0, $"生成失败: {ex.Message}");
            throw;
        }
        finally
        {
            CleanupTempFile(tempPsdPath);
            CleanupTempFile(tempPdfPath);
        }
    }

    // ... [EmbedFixedAssetAsSmartObject 方法保持不变] ...
    /// <summary>
    /// 架构师修正版: 采用等比缩放与严格 DPI 同步，解决 Save 崩溃问题
    /// </summary>
    private void EmbedFixedAssetAsSmartObject(string targetPsdPath, string assetPath, PackagingDimensions dim)
    {
        // 1. 基础校验
        if (!File.Exists(assetPath)) 
        {
            Console.WriteLine($"[Error] 固定图标源文件不存在: {assetPath}");
            return;
        }
        if (!File.Exists(targetPsdPath)) return;

        string tempOutputPath = targetPsdPath + ".tmp";
        
        using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
        using (var srcPsd = (PsdImage)Aspose.PSD.Image.Load(assetPath))
        {
            float targetDpiX = (float)targetImage.HorizontalResolution;
            float targetDpiY = (float)targetImage.VerticalResolution;
            const double cmToInch = 1.0 / 2.54;

            int maxBoxWidth = Math.Max(1, (int)Math.Round(ICON_WIDTH_CM * cmToInch * targetDpiX));
            int maxBoxHeight = Math.Max(1, (int)Math.Round(ICON_HEIGHT_CM * cmToInch * targetDpiY));

            double scaleX = (double)maxBoxWidth / srcPsd.Width;
            double scaleY = (double)maxBoxHeight / srcPsd.Height;
            double scale = Math.Min(scaleX, scaleY); 

            int newWidth = Math.Max(1, (int)Math.Round(srcPsd.Width * scale));
            int newHeight = Math.Max(1, (int)Math.Round(srcPsd.Height * scale));

            var pos = CalculateFixedIconPosition(targetImage, dim, newWidth, newHeight);
            int destLeft = pos.X;
            int destTop = pos.Y;

            var placeholder = targetImage.AddRegularLayer();
            placeholder.DisplayName = "FixedIcon_Placeholder";
            placeholder.Left = destLeft;
            placeholder.Top = destTop;
            placeholder.Right = destLeft + newWidth;
            placeholder.Bottom = destTop + newHeight;

            var transparentPixels = new int[newWidth * newHeight];
            placeholder.SaveArgb32Pixels(new Aspose.PSD.Rectangle(0, 0, newWidth, newHeight), transparentPixels);

            var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "FixedIcon_SmartObject";

            if (srcPsd.Width != newWidth || srcPsd.Height != newHeight)
            {
                srcPsd.Resize(newWidth, newHeight, ResizeType.LanczosResample);
            }
            
            srcPsd.HorizontalResolution = targetDpiX;
            srcPsd.VerticalResolution = targetDpiY;

            var resolution = new ResolutionSetting(targetDpiX, targetDpiY);
            smartLayer.ReplaceContents(srcPsd, resolution);

            smartLayer.Left = destLeft;
            smartLayer.Top = destTop;

            var saveOptions = new PsdOptions
            {
                CompressionMethod = CompressionMethod.RLE,
                ColorMode = ColorModes.Rgb
            };
            
            targetImage.Save(tempOutputPath, saveOptions);
        }

        if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
        File.Move(tempOutputPath, targetPsdPath);
    }

    /// <summary>
    /// ✅ 新增: 专门用于处理品牌 Logo 的置入逻辑
    /// 复用了 EmbedFixedAssetAsSmartObject 的核心算法，但使用了 Logo 专用的尺寸和定位逻辑
    /// </summary>
    private void EmbedLogoAsSmartObject(string targetPsdPath, string assetPath, PackagingDimensions dim)
    {
        if (!File.Exists(assetPath)) return;
        if (!File.Exists(targetPsdPath)) return;

        string tempOutputPath = targetPsdPath + ".tmp";

        using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
        using (var srcPsd = (PsdImage)Aspose.PSD.Image.Load(assetPath))
        {
            float targetDpiX = (float)targetImage.HorizontalResolution;
            float targetDpiY = (float)targetImage.VerticalResolution;
            const double cmToInch = 1.0 / 2.54;

            // 1. 计算 Logo 的最大像素容器 (使用 LOGO_MAX_WIDTH_CM)
            int maxBoxWidth = Math.Max(1, (int)Math.Round(LOGO_MAX_WIDTH_CM * cmToInch * targetDpiX));
            int maxBoxHeight = Math.Max(1, (int)Math.Round(LOGO_MAX_HEIGHT_CM * cmToInch * targetDpiY));

            // 2. 等比缩放计算
            double scaleX = (double)maxBoxWidth / srcPsd.Width;
            double scaleY = (double)maxBoxHeight / srcPsd.Height;
            double scale = Math.Min(scaleX, scaleY); // 确保完全放入且不变形

            int newWidth = Math.Max(1, (int)Math.Round(srcPsd.Width * scale));
            int newHeight = Math.Max(1, (int)Math.Round(srcPsd.Height * scale));

            // 3. 计算 Logo 位置 (主面板顶部居中)
            var pos = CalculateLogoPosition(targetImage, dim, newWidth, newHeight);
            int destLeft = pos.X;
            int destTop = pos.Y;

            // 4. 创建占位图层
            var placeholder = targetImage.AddRegularLayer();
            placeholder.DisplayName = "BrandLogo_Placeholder";
            placeholder.Left = destLeft;
            placeholder.Top = destTop;
            placeholder.Right = destLeft + newWidth;
            placeholder.Bottom = destTop + newHeight;

            // 填充透明像素初始化
            var transparentPixels = new int[newWidth * newHeight];
            placeholder.SaveArgb32Pixels(new Aspose.PSD.Rectangle(0, 0, newWidth, newHeight), transparentPixels);

            // 5. 转为智能对象并替换内容
            var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "BrandLogo_SmartObject";

            // 调整源图尺寸与 DPI
            if (srcPsd.Width != newWidth || srcPsd.Height != newHeight)
            {
                srcPsd.Resize(newWidth, newHeight, ResizeType.LanczosResample);
            }
            srcPsd.HorizontalResolution = targetDpiX;
            srcPsd.VerticalResolution = targetDpiY;

            // 执行替换
            var resolution = new ResolutionSetting(targetDpiX, targetDpiY);
            smartLayer.ReplaceContents(srcPsd, resolution);

            // 重新校准位置
            smartLayer.Left = destLeft;
            smartLayer.Top = destTop;

            // 6. 保存
            var saveOptions = new PsdOptions
            {
                CompressionMethod = CompressionMethod.RLE,
                ColorMode = ColorModes.Rgb
            };
            targetImage.Save(tempOutputPath, saveOptions);
        }

        if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
        File.Move(tempOutputPath, targetPsdPath);
    }

    /// <summary>
    /// ✅ [修正版] 使用 Aspose.Words 渲染 PDF 条形码，避开 Linux 下 System.Drawing 崩溃问题
    /// </summary>
    private void EmbedBarcodePdfAsSmartObject(string targetPsdPath, string pdfPath, PackagingDimensions dim)
    {
        if (!File.Exists(pdfPath) || !File.Exists(targetPsdPath)) return;

        string tempOutputPath = targetPsdPath + ".tmp";

        // 加上 try-catch 保护，防止图形渲染失败导致整个 API 502 崩溃
        try
        {
            // 1. 使用 Aspose.Words 加载 PDF 文档 (更稳定的跨平台渲染引擎)
            var pdfAsWordDoc = new Aspose.Words.Document(pdfPath);

            using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
            {
                float targetDpiX = (float)targetImage.HorizontalResolution;
                float targetDpiY = (float)targetImage.VerticalResolution;

                // 2. 配置渲染选项 (输出为 PNG)
                var saveOptions = new Aspose.Words.Saving.ImageSaveOptions(Aspose.Words.SaveFormat.Png)
                {
                    PageSet = new Aspose.Words.Saving.PageSet(0), // 只渲染第一页
                    Resolution = targetDpiX,                       // 保持 DPI 一致
                    UseHighQualityRendering = true,
                    // 注意：这里的 System.Drawing.Color 是结构体，在 .NET 8 中通常安全，只有 GDI+ 绘图调用才危险
                    PaperColor = System.Drawing.Color.Transparent 
                };

                using var pageImageStream = new MemoryStream();

                // 3. 执行渲染
                pdfAsWordDoc.Save(pageImageStream, saveOptions);
                pageImageStream.Position = 0;

                // 4. 将渲染好的图片作为光栅图像加载
                using var loadedImage = Aspose.PSD.Image.Load(pageImageStream);
                var raster = (RasterImage)loadedImage;
                raster.CacheData();

                const double cmToInch = 1.0 / 2.54;
                int targetWidthPx = (int)Math.Round(BARCODE_WIDTH_CM * cmToInch * targetDpiX);
                int targetHeightPx = (int)Math.Round(BARCODE_HEIGHT_CM * cmToInch * targetDpiY);

                // 生成缩放后的 PSD 容器
                using var srcPsd = CreateScaledContainer(raster, targetWidthPx, targetHeightPx, targetDpiX, targetDpiY);

                // 计算位置
                var pos = CalculateBarcodePosition(targetImage, dim, targetWidthPx, targetHeightPx);

                // 创建占位图层
                var placeholder = targetImage.AddRegularLayer();
                placeholder.DisplayName = "Barcode_Placeholder";
                placeholder.Left = pos.X;
                placeholder.Top = pos.Y;
                placeholder.Right = pos.X + targetWidthPx;
                placeholder.Bottom = pos.Y + targetHeightPx;

                // 转换为智能对象
                var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
                smartLayer.DisplayName = "Barcode_SmartObject";
                
                // 替换内容
                var resolutionSetting = new ResolutionSetting(targetDpiX, targetDpiY);
                smartLayer.ReplaceContents(srcPsd, resolutionSetting);
                smartLayer.ContentsBounds = new Aspose.PSD.Rectangle(0, 0, targetWidthPx, targetHeightPx);

                // 保存 PSD
                targetImage.Save(tempOutputPath, new PsdOptions { 
                    CompressionMethod = CompressionMethod.RLE 
                });
            } 

            // 成功后覆盖原文件
            if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
            File.Move(tempOutputPath, targetPsdPath);
        }
        catch (Exception ex)
        {
            // 兜底保护：即使渲染失败，只记录日志，不让 API 崩溃
            Console.WriteLine($"[Warning] 条形码渲染失败，已跳过置入: {ex.Message}");
            if (File.Exists(tempOutputPath)) try { File.Delete(tempOutputPath); } catch { }
        }
    }

    // ... [CreateScaledContainer, CalculateBarcodePosition 等方法保持不变] ...
    private PsdImage CreateScaledContainer(RasterImage source, int width, int height, float dpiX, float dpiY)
    {
        var container = new PsdImage(width, height);
        container.SetResolution(dpiX, dpiY);

        double scaleX = (double)width / source.Width;
        double scaleY = (double)height / source.Height;
        double scale = Math.Min(scaleX, scaleY); 

        int newW = Math.Max(1, (int)(source.Width * scale));
        int newH = Math.Max(1, (int)(source.Height * scale));

        if (source.Width != newW || source.Height != newH)
        {
            source.Resize(newW, newH, ResizeType.LanczosResample);
        }

        var layer = container.AddRegularLayer();
        layer.Left = 0; layer.Top = 0; layer.Right = width; layer.Bottom = height;
        
        int offX = (width - newW) / 2;
        int offY = (height - newH) / 2;

        var pixels = source.LoadArgb32Pixels(source.Bounds);
        var destRect = new Rectangle(offX, offY, newW, newH);
        layer.SaveArgb32Pixels(destRect, pixels);

        return container;
    }

   // ====================================================================================
    // ⬇️ 核心定位算法重构：基于折线(Fold Lines)的绝对坐标系，确保秩序感
    // ====================================================================================

    /// <summary>
    /// ✅ [Logo 定位] 锁定 Front Panel (正面)
    /// 垂直策略：顶部折线向下 12% (视觉重心)
    /// </summary>
    private Point CalculateLogoPosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        // 1. 获取基础坐标
        var X = CmToPixels(dim.Length); // 正面宽度
        var Y = CmToPixels(dim.Height); // 正面高度
        var Z = CmToPixels(dim.Width);  // 侧面宽度
        var A = CmToPixels(dim.BleedLeftRight); // 粘口
        var B = CmToPixels(dim.BleedTopBottom); // 顶部出血

        // 2. 锁定 Front Panel 区域 (根据 DrawStructureLayers 逻辑，正面是第2个面)
        // 左边界 = 粘口(A) + 左侧面(Z)
        int panelLeft = A + Z;
        
        // 3. 计算绝对水平居中
        int centerX = panelLeft + (X / 2);
        int destX = centerX - (widthPx / 2) + 300;

        // 4. 计算垂直位置 (严格基于 Top Fold 折线)
        // Top Fold = B + Z (忽略出血和盖板，直接找盒身顶线)
        int topFoldY = B + Z;
        
        // 留白 = 盒身高度的 12% (黄金视觉位)
        int marginTop = (int)(Y * 0.12);
        
        int destY = topFoldY + marginTop;

        return new Point(destX, destY);
    }

    /// <summary>
    /// ✅ [条形码定位] 锁定 Back Panel (背面)
    /// 垂直策略：底部折线向上 10% (底部留白)
    /// </summary>
    private Point CalculateBarcodePosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        // 1. 锁定 Back Panel 区域 (第4个面)
        // 左边界 = 粘口(A) + 左侧(Z) + 正面(X) + 右侧(Z)
        int panelLeft = A + (2 * Z) + X;
        
        // 2. 水平居中 (Back Panel 宽度也是 X)
        int centerX = panelLeft + (X / 2);
        int destX = centerX - (widthPx / 2) ;

        // 3. 垂直定位 (严格基于 Bottom Fold 折线)
        // Bottom Fold = B + Z + Y
        int bottomFoldY = B + Z + Y;

        // 底部留白 = 10%
        int marginBottom = (int)(Y * 0.10);

        int destY = bottomFoldY - marginBottom - heightPx;

        return new Point(destX, destY);
    }

    /// <summary>
    /// ✅ [固定图标定位] 锁定 Back Panel (背面)
    /// 垂直策略：紧贴条形码上方 (形成整齐的堆叠队列)
    /// </summary>
    private Point CalculateFixedIconPosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        // 1. 锁定 Back Panel 区域
        int panelLeft = A + (2 * Z) + X;

        // 2. 水平居中
        int centerX = panelLeft + (X / 2);
        int destX = centerX - (widthPx / 2) + 300;

        // 3. 垂直定位 (堆叠逻辑)
        // 先计算条形码的顶部位置作为锚点
        // 注意：这里必须复用条形码的计算逻辑，保证严丝合缝
        
        int bottomFoldY = B + Z + Y;
        int marginBottom = (int)(Y * 0.10);
        
        // 预估条形码高度 (为了计算锚点，这里需要知道条形码实际像素高度)
        // 在 GenerateInternalAsync 中，条形码是动态生成的，这里我们使用标准常量反推，或者更安全的做法是留出足够的“安全区”
        // 更好的方案：图标位于 底部折线向上 25% 处 (避开条形码区域)
        
        // 方案 B：相对比例定位 (更稳健，防止条形码高度变化导致重叠)
        // 设条形码区域约占底部 20%，图标放在底部 22%~25% 的位置
        int iconBottomMargin = (int)(Y * 0.28); // 底部向上 28%
        
        int destY = bottomFoldY - iconBottomMargin - heightPx + 300;

        return new Point(destX, destY);
    }

   
    private void DrawStructureLayers(PsdImage psdImage, int X, int Y, int Z, int A, int B, int C, Action<int, string>? onProgress)
    {
        CreateShapeLayer(psdImage, "BG", (2 * X) + (2 * Z) + (2 * A), Y + (4 * C), 0, B + Z - (2 * C), Color.White);

        onProgress?.Invoke(15, "正在绘制侧面板...");
        CreateShapeLayer(psdImage, "left", A + X, Y + (4 * C), 0, B + Z - (2 * C), Color.White);
        CreateShapeLayer(psdImage, "front", X, Y + (4 * C), A + Z, B + Z - (2 * C), Color.White);

        onProgress?.Invoke(25, "正在绘制主面板...");
        CreateShapeLayer(psdImage, "right", X, Y + (4 * C), A + Z + X, B + Z - (2 * C), Color.White);
        CreateShapeLayer(psdImage, "back", A + X, Y + (4 * C), A + (2 * Z) + X, B + Z - (2 * C), Color.White);

        onProgress?.Invoke(35, "正在绘制顶底盖...");
        CreateShapeLayer(psdImage, "top", X, Z, A + Z, B, Color.White);
        CreateShapeLayer(psdImage, "bottom", X, Z, A + (2 * Z) + X, B + Z + Y, Color.White);
    }

    private void DrawInfoPanelAssets(PsdImage psdImage, PackagingAssets assets, PackagingDimensions dim)
    {
        var info = assets.Texts.InfoPanel;
        var main = assets.Texts.MainPanel;
        
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);
        var C = CmToPixels(dim.InnerBleed);

        int startX = A + (2 * Z) + X;
        int startY = B + Z - (2 * C);
        int panelWidth = A + X;

        int padding = 30;
        int currentY = startY + padding;
        int textAreaWidth = panelWidth - (2 * padding);
        float fontSize = 6f;

        if (info != null)
        {
            //TODO 增加 产品英文名	PRODUCT_NAME_TXT

            CreateRichTextLayer(psdImage, "INGREDIENTS_TXT", "INGREDIENTS:", info.Ingredients,
                new Rectangle(startX + padding, currentY, textAreaWidth, 100), fontSize);
            currentY += 110;

          //  CreateRichTextLayer(psdImage, "Directions", "DIRECTIONS:", info.Directions,
           //     new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
            currentY += 90;

            CreateRichTextLayer(psdImage, "WARNINGS_TXT", "WARNINGS:", info.Warnings,
                new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
            currentY += 90;

            CreateRichTextLayer(psdImage, "MANUFACTURER_TXT", "MANUFACTURER:", main.Manufacturer,
                new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
            currentY += 60;

            CreateRichTextLayer(psdImage, "MANUFACTURER_ADD_TXT", "ADDRESS:", main.Address,
                new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
            currentY += 60;

            CreateRichTextLayer(psdImage, "MADE_IN _TXT", "MADE IN:", info.Origin,
                new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
        }

        if (assets.Texts.MainPanel != null && !string.IsNullOrWhiteSpace(assets.Texts.MainPanel.CapacityInfoBack))
        {
            int areaHeight = 100;
            int panelVisualBottom = startY + Y + (2 * C); 
            int capY = panelVisualBottom - padding - areaHeight;

            CreateRichTextLayer(psdImage, "CapacityInfoBack", "", assets.Texts.MainPanel.CapacityInfoBack,
                new Rectangle(startX + padding, capY, textAreaWidth, areaHeight), 10f);
        }
    }

    private void DrawMainPanelAssets(PsdImage psdImage, PackagingAssets assets, PackagingDimensions dim)
    {
        var main = assets.Texts.MainPanel;
        if (main == null) return;

        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        int panelStartX = A + Z;
        int panelStartY = B + Z; 
        
        int padding = 40;
        int contentWidth = X - (2 * padding);
        int contentStartX = panelStartX + padding;

        if (main.SellingPoints != null && main.SellingPoints.Count > 0)
        {
            int startY = panelStartY + (int)(Y * 0.3);
            int lineHeight = 60;
            float fontSize = 8f;

            for (int i = 0; i < main.SellingPoints.Count; i++)
            {
                var pointText = main.SellingPoints[i];
                if (string.IsNullOrWhiteSpace(pointText)) continue;

                var textToShow = "• " + pointText;
                var rect = new Rectangle(contentStartX, startY + (i * lineHeight), contentWidth, lineHeight);
                CreateRichTextLayer(psdImage, $"SellingPoint_{i + 1}", "", textToShow, rect, fontSize);
            }
        }

        int currentBottomY = panelStartY + Y - padding;
        int capacityHeight = 100;

        if (!string.IsNullOrWhiteSpace(main.CapacityInfo))
        {
            currentBottomY -= capacityHeight;
            var rect = new Rectangle(contentStartX, currentBottomY, contentWidth, capacityHeight);
            CreateRichTextLayer(psdImage, "CapacityInfo_Front", "", main.CapacityInfo, rect, 10f);
        }
    }

    private void AddGuidelines(PsdImage psdImage, int X, int Y, int Z, int A, int B, int C)
    {
        var horizontalWidth = 2 * X + 2 * Z + 2 * A;
        var verticalHeight = Y + 2 * Z + 2 * B - 4 * C;
        var topIndex = psdImage.Layers.Length;
        var lineGroup = psdImage.AddLayerGroup("GuidelineGroup", topIndex, false);

        CreateGuidelineLayer(psdImage, "guideline_Y001", 0, B, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y002", 0, B + Z - 2 * C, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y003", 0, B + Z, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y004", 0, B + Z + Y, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y005", 0, B + Z + Y + 2 * C, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y006", 0, B + Z + Y + Z, horizontalWidth, 1, lineGroup);

        CreateGuidelineLayer(psdImage, "guideline_X001", A, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X002", A + Z, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X003", A + Z + X, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X004", A + 2 * Z + X, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X006", A + 2 * Z + 2 * X, 0, 1, verticalHeight, lineGroup);
    }

    private void CreateGuidelineLayer(PsdImage psdImage, string layerName, int x, int y, int width, int height, LayerGroup lineGroup)
    {
        var magentaColor = Color.FromArgb(255, 0, 108);
        CreateWhiteRectangleLineLayer(psdImage, layerName, new Rectangle(x, y, width, height), magentaColor, lineGroup);
    }

    private void CreateWhiteRectangleLineLayer(PsdImage psdImage, string layerName, Rectangle rect, Color layerColor, LayerGroup lineGroup)
    {
        if (psdImage == null) return;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        try
        {
            var layer = FillLayer.CreateInstance(FillType.Color);
            if (layer == null) return;
            layer.DisplayName = layerName ?? "guideline";
            lineGroup.AddLayer(layer);
            var vectorPath = VectorDataProvider.CreateVectorPathForLayer(layer);
            if (vectorPath == null) return;
            vectorPath.FillColor = layerColor;
            var shape = new PathShapeGen();
            shape.Points.Add(new BezierKnot(new PointF(rect.X, rect.Y), true));
            shape.Points.Add(new BezierKnot(new PointF(rect.X + rect.Width, rect.Y), true));
            shape.Points.Add(new BezierKnot(new PointF(rect.X + rect.Width, rect.Y + rect.Height), true));
            shape.Points.Add(new BezierKnot(new PointF(rect.X, rect.Y + rect.Height), true));
            vectorPath.Shapes.Add(shape);
            VectorDataProvider.UpdateLayerFromVectorPath(layer, vectorPath, true);
            layer.AddLayerMask(null);
        }
        catch (Exception ex) { Console.WriteLine($"CreateWhiteRectangleLineLayer exception: {ex.Message}"); }
    }

    private void CreateShapeLayer(PsdImage psdImage, string layerName, int width, int height, int x, int y, Color layerColor)
    {
        if (psdImage == null) return;
        if (width <= 0 || height <= 0) return;
        try
        {
            var layer = FillLayer.CreateInstance(FillType.Color);
            if (layer == null) return;
            layer.DisplayName = layerName;
            psdImage.AddLayer(layer);
            var vectorPath = VectorDataProvider.CreateVectorPathForLayer(layer);
            if (vectorPath == null) return;
            vectorPath.FillColor = layerColor;
            var shape = new PathShapeGen();
            var p1 = new PointF(x, y);
            var p2 = new PointF(x + width, y);
            var p3 = new PointF(x + width, y + height);
            var p4 = new PointF(x, y + height);
            shape.Points.Add(new BezierKnot(p1, true));
            shape.Points.Add(new BezierKnot(p2, true));
            shape.Points.Add(new BezierKnot(p3, true));
            shape.Points.Add(new BezierKnot(p4, true));
            vectorPath.Shapes.Add(shape);
            VectorDataProvider.UpdateLayerFromVectorPath(layer, vectorPath, true);
            layer.AddLayerMask(null);
        }
        catch (Exception ex) { Console.WriteLine($"Error creating layer {layerName}: {ex.Message}"); }
    }

    private void CreateRichTextLayer(PsdImage psdImage, string layerName, string label, string content, Rectangle rect, float fontSizePt)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        try
        {
            var textLayer = psdImage.AddTextLayer(layerName, rect);
            var textData = textLayer.TextData;
            float fontSizePixels = PtToPixels(fontSizePt);
            textData.Items[0].Style.FontSize = fontSizePixels;
            textData.Items[0].Paragraph.Justification = JustificationMode.Left;
            
            string fontName = "Arial";

            if (!string.IsNullOrEmpty(label))
            {
                var labelPortion = textData.Items[0];
                labelPortion.Text = label + " ";
                labelPortion.Style.FauxBold = true;
                labelPortion.Style.FontName = fontName;
                labelPortion.Style.FillColor = Color.Black;
                
                var contentPortion = textData.ProducePortion();
                contentPortion.Text = content;
                contentPortion.Style.FontSize = fontSizePixels;
                contentPortion.Style.FontName = fontName;
                contentPortion.Style.FauxBold = false;
                contentPortion.Style.FillColor = Color.Black;
                textData.AddPortion(contentPortion);
            }
            else
            {
                var portion = textData.Items[0];
                portion.Text = content;
                portion.Style.FontName = fontName;
                portion.Style.FontSize = fontSizePixels;
                portion.Style.FauxBold = false;
                portion.Style.FillColor = Color.Black;
            }
            textData.UpdateLayerData();
        }
        catch (Exception ex) { Console.WriteLine($"Error creating text layer {layerName}: {ex.Message}"); }
    }

    private int CmToPixels(double cm) => (int)Math.Round((cm / 2.54) * DPI);
    private float PtToPixels(float pt) => (pt * DPI) / 72f;

    private void CleanupTempFile(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try { File.Delete(path); } catch { /* 忽略删除失败 */ }
        }
    }
}