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

    // Logo 最大限制尺寸
    private const double LOGO_MAX_WIDTH_CM = 8.0;
    private const double LOGO_MAX_HEIGHT_CM = 4.0;

    private static readonly HttpClient _httpClient = new HttpClient();
    
    static AsposePsdGenerator()
    {
        try
        {
            var fontFolders = new List<string>();

            // 1. 添加自定义字体目录
            string customFontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Fonts");
            if (Directory.Exists(customFontsDir))
            {
                fontFolders.Add(customFontsDir);
            }

            // 2. Windows 系统字体
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                fontFolders.Add(@"C:\Windows\Fonts"); 
            }
            else
            {
                // Linux 字体
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

            // --- 阶段 3.5: 处理动态品牌 Logo ---
            var brandName = assets.Texts?.MainPanel?.BrandName;
            if (!string.IsNullOrWhiteSpace(brandName))
            {
                var safeBrandName = string.Join("_", brandName.Split(Path.GetInvalidFileNameChars()));
                string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Logo", $"{safeBrandName}.psd");

                if (File.Exists(logoPath))
                {
                    onProgress?.Invoke(78, $"正在置入品牌 Logo: {safeBrandName}...");
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

    private void EmbedFixedAssetAsSmartObject(string targetPsdPath, string assetPath, PackagingDimensions dim)
    {
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

            int maxBoxWidth = Math.Max(1, (int)Math.Round(LOGO_MAX_WIDTH_CM * cmToInch * targetDpiX));
            int maxBoxHeight = Math.Max(1, (int)Math.Round(LOGO_MAX_HEIGHT_CM * cmToInch * targetDpiY));

            double scaleX = (double)maxBoxWidth / srcPsd.Width;
            double scaleY = (double)maxBoxHeight / srcPsd.Height;
            double scale = Math.Min(scaleX, scaleY); 

            int newWidth = Math.Max(1, (int)Math.Round(srcPsd.Width * scale));
            int newHeight = Math.Max(1, (int)Math.Round(srcPsd.Height * scale));

            var pos = CalculateLogoPosition(targetImage, dim, newWidth, newHeight);
            int destLeft = pos.X;
            int destTop = pos.Y;

            var placeholder = targetImage.AddRegularLayer();
            placeholder.DisplayName = "BrandLogo_Placeholder";
            placeholder.Left = destLeft;
            placeholder.Top = destTop;
            placeholder.Right = destLeft + newWidth;
            placeholder.Bottom = destTop + newHeight;

            var transparentPixels = new int[newWidth * newHeight];
            placeholder.SaveArgb32Pixels(new Aspose.PSD.Rectangle(0, 0, newWidth, newHeight), transparentPixels);

            var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "BrandLogo_SmartObject";

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

    private void EmbedBarcodePdfAsSmartObject(string targetPsdPath, string pdfPath, PackagingDimensions dim)
    {
        if (!File.Exists(pdfPath) || !File.Exists(targetPsdPath)) return;

        string tempOutputPath = targetPsdPath + ".tmp";

        try
        {
            var pdfAsWordDoc = new Aspose.Words.Document(pdfPath);

            using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
            {
                float targetDpiX = (float)targetImage.HorizontalResolution;
                float targetDpiY = (float)targetImage.VerticalResolution;

                var saveOptions = new Aspose.Words.Saving.ImageSaveOptions(Aspose.Words.SaveFormat.Png)
                {
                    PageSet = new Aspose.Words.Saving.PageSet(0), 
                    Resolution = targetDpiX,                       
                    UseHighQualityRendering = true,
                    PaperColor = System.Drawing.Color.Transparent 
                };

                using var pageImageStream = new MemoryStream();
                pdfAsWordDoc.Save(pageImageStream, saveOptions);
                pageImageStream.Position = 0;

                using var loadedImage = Aspose.PSD.Image.Load(pageImageStream);
                var raster = (RasterImage)loadedImage;
                raster.CacheData();

                const double cmToInch = 1.0 / 2.54;
                int targetWidthPx = (int)Math.Round(BARCODE_WIDTH_CM * cmToInch * targetDpiX);
                int targetHeightPx = (int)Math.Round(BARCODE_HEIGHT_CM * cmToInch * targetDpiY);

                using var srcPsd = CreateScaledContainer(raster, targetWidthPx, targetHeightPx, targetDpiX, targetDpiY);

                var pos = CalculateBarcodePosition(targetImage, dim, targetWidthPx, targetHeightPx);

                var placeholder = targetImage.AddRegularLayer();
                placeholder.DisplayName = "Barcode_Placeholder";
                placeholder.Left = pos.X;
                placeholder.Top = pos.Y;
                placeholder.Right = pos.X + targetWidthPx;
                placeholder.Bottom = pos.Y + targetHeightPx;

                var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
                smartLayer.DisplayName = "BARCODE"; 
                
                var resolutionSetting = new ResolutionSetting(targetDpiX, targetDpiY);
                smartLayer.ReplaceContents(srcPsd, resolutionSetting);
                smartLayer.ContentsBounds = new Aspose.PSD.Rectangle(0, 0, targetWidthPx, targetHeightPx);

                targetImage.Save(tempOutputPath, new PsdOptions { 
                    CompressionMethod = CompressionMethod.RLE 
                });
            } 

            if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
            File.Move(tempOutputPath, targetPsdPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Warning] 条形码渲染失败，已跳过置入: {ex.Message}");
            if (File.Exists(tempOutputPath)) try { File.Delete(tempOutputPath); } catch { }
        }
    }

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
    // ⬇️ 核心定位算法
    // ====================================================================================

    private Point CalculateLogoPosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        int panelLeft = A + Z;
        
        int centerX = panelLeft + (X / 2);
        int destX = centerX - (widthPx / 2) + 300;

        int topFoldY = B + Z;
        int marginTop = (int)(Y * 0.12);
        int destY = topFoldY + marginTop;

        return new Point(destX, destY);
    }

    private Point CalculateBarcodePosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        // Right Side 区域 (第3个面)
        int panelLeft = A + Z + X;
        int panelWidth = Z; 
        
        int centerX = panelLeft + (panelWidth / 2);
        int destX = centerX - (widthPx / 2) ;

        // 垂直定位 (底部折线向上 10%)
        int bottomFoldY = B + Z + Y;
        int marginBottom = (int)(Y * 0.10);

        int destY = bottomFoldY - marginBottom - heightPx;

        return new Point(destX, destY);
    }

    private Point CalculateFixedIconPosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        int panelLeft = A + (2 * Z) + X;

        int centerX = panelLeft + (X / 2);
        int destX = centerX - (widthPx / 2) + 300;

        int bottomFoldY = B + Z + Y;
        int iconBottomMargin = (int)(Y * 0.28); 
        
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

    // ✅ [修改] 重构 DrawInfoPanelAssets，整体下移以避开出血位
    private void DrawInfoPanelAssets(PsdImage psdImage, PackagingAssets assets, PackagingDimensions dim)
    {
        var info = assets.Texts.InfoPanel;
        var main = assets.Texts.MainPanel;
        
        if (info == null && main == null) return;

        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);
        var C = CmToPixels(dim.InnerBleed);

        // 核心修正：使用 Top Fold Line (B + Z) 作为基准，并增加 80px 的安全距离
        // 之前是 B + Z - 2*C，这会导致内容偏上进入出血区域
        int topFoldY = B + Z;
        int safeTopMargin = 80; // ✅ 增加顶部安全边距

        // --- 1. 背面 (Back Panel) ---
        int backStartX = A + (2 * Z) + X;
        int backStartY = topFoldY + safeTopMargin; // ✅ 从折线下方 80px 开始
        int backPanelWidth = A + X; 

        int padding = 30;
        int currentY = backStartY;
        int textAreaWidth = backPanelWidth - (2 * padding);
        float fontSize = 6f;

        if (info != null)
        {
            // 产品英文名
            CreateRichTextLayer(psdImage, "PRODUCT_NAME_TXT", "PRODUCT NAME:", info.ProductName,
                new Rectangle(backStartX + padding, currentY, textAreaWidth, 50), fontSize);
            currentY += 60;

            // 成分
            CreateRichTextLayer(psdImage, "INGREDIENTS_TXT", "INGREDIENTS:", info.Ingredients,
                new Rectangle(backStartX + padding, currentY, textAreaWidth, 100), fontSize);
            currentY += 110;

            // 警告语
            CreateRichTextLayer(psdImage, "WARNINGS_TXT", "WARNINGS:", info.Warnings,
                new Rectangle(backStartX + padding, currentY, textAreaWidth, 80), fontSize);
            currentY += 90;

            // 制造商
            if (main != null)
            {
                CreateRichTextLayer(psdImage, "MANUFACTURER_TXT", "MANUFACTURER:", main.Manufacturer,
                    new Rectangle(backStartX + padding, currentY, textAreaWidth, 50), fontSize);
                currentY += 60;

                // 制造商地址
                CreateRichTextLayer(psdImage, "MANUFACTURER_ADD_TXT", "ADDRESS:", main.Address,
                    new Rectangle(backStartX + padding, currentY, textAreaWidth, 50), fontSize);
                currentY += 60;
            }

            // 保质期
            CreateRichTextLayer(psdImage, "SHELF_LIFE_TXT", "SHELF LIFE:", info.ShelfLife,
                new Rectangle(backStartX + padding, currentY, textAreaWidth, 50), fontSize);
            currentY += 60;

            // 原产国
            CreateRichTextLayer(psdImage, "MADE_IN _TXT", "MADE IN:", info.Origin,
                new Rectangle(backStartX + padding, currentY, textAreaWidth, 50), fontSize);
            
            // 含量（背面）- 保持底部对齐逻辑
            if (main != null && !string.IsNullOrWhiteSpace(main.CapacityInfoBack))
            {
                int areaHeight = 100;
                // 底部折线 = B + Z + Y
                int bottomFoldY = B + Z + Y; 
                int capY = bottomFoldY - padding - areaHeight; // ✅ 确保在底部折线之上

                CreateRichTextLayer(psdImage, "NET_BACK_TXT", "", main.CapacityInfoBack,
                    new Rectangle(backStartX + padding, capY, textAreaWidth, areaHeight), 10f);
            }
        }

        // --- 2. 右侧面 (Right Side Panel) ---
        int rightStartX = A + Z + X;
        int rightStartY = topFoldY + safeTopMargin; // ✅ 同样整体下移
        int rightPanelWidth = Z;
        
        int rightCurrentY = rightStartY;
        int rightTextAreaWidth = rightPanelWidth - (2 * padding);

        if (info != null)
        {
            // 建议使用方法
            CreateRichTextLayer(psdImage, "SAFEUSE_TXT", "DIRECTIONS:", info.Directions,
                new Rectangle(rightStartX + padding, rightCurrentY, rightTextAreaWidth, 150), fontSize);
            rightCurrentY += 160;

            // 产品优势/利益点
            CreateRichTextLayer(psdImage, "FUNCTIONS&BENEFITS_TXT", "BENEFITS:", info.Benefits,
                new Rectangle(rightStartX + padding, rightCurrentY, rightTextAreaWidth, 150), fontSize);
        }
    }

    // ✅ [修改] 重构 DrawMainPanelAssets，整体下移
    private void DrawMainPanelAssets(PsdImage psdImage, PackagingAssets assets, PackagingDimensions dim)
    {
        var main = assets.Texts.MainPanel;
        var info = assets.Texts.InfoPanel;
        if (main == null) return;

        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);

        // 正面区域
        int panelStartX = A + Z;
        
        // 核心修正：基准线设为 Top Fold (B + Z)，并加大 Padding
        int topFoldY = B + Z;
        int topPadding = 80; // ✅ 增加顶部安全边距

        int contentWidth = X - (2 * 40);
        int contentStartX = panelStartX + 40;

        // 英文产品名（正面）
        if (!string.IsNullOrWhiteSpace(info.ProductName))
        {
            int titleHeight = 80;
            var rect = new Rectangle(contentStartX, topFoldY + topPadding, contentWidth, titleHeight);
            CreateRichTextLayer(psdImage, "PRODUCT_NAME_FRONT_TXT", "", info.ProductName, rect, 12f);
        }

        // 正面卖点文案
        if (main.SellingPoints != null && main.SellingPoints.Count > 0)
        {
            int startY = topFoldY + (int)(Y * 0.35); // 相对 Top Fold 保持 35% 下移
            int lineHeight = 60;
            float fontSize = 8f;

            for (int i = 0; i < main.SellingPoints.Count; i++)
            {
                var pointText = main.SellingPoints[i];
                if (string.IsNullOrWhiteSpace(pointText)) continue;

                var textToShow = "• " + pointText;
                var rect = new Rectangle(contentStartX, startY + (i * lineHeight), contentWidth, lineHeight);
                
                CreateRichTextLayer(psdImage, $"SELLINGPOINT#{i + 1}_TXT", "", textToShow, rect, fontSize);
            }
        }

        // 含量（正面）- 底部定位
        if (!string.IsNullOrWhiteSpace(main.CapacityInfo))
        {
            int capacityHeight = 100;
            // 底部折线 = B + Z + Y
            int bottomFoldY = B + Z + Y;
            int currentBottomY = bottomFoldY - 40 - capacityHeight; // ✅ 确保在底部折线之上
            
            var rect = new Rectangle(contentStartX, currentBottomY, contentWidth, capacityHeight);
            CreateRichTextLayer(psdImage, "NET_FRONT_TXT", "", main.CapacityInfo, rect, 10f);
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