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
    private const double BARCODE_WIDTH_CM = 6.0; 
    private const double BARCODE_HEIGHT_CM = 3.0; 

    // --- 固定图标尺寸 ---
    private const double ICON_WIDTH_CM = 5.0;
    private const double ICON_HEIGHT_CM = 2.8;

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

            // 2. ✅ 关键修正：如果是 Windows，必须显式把系统字体目录加回来
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                fontFolders.Add(@"C:\Windows\Fonts"); 
            }
            else
            {
                // Linux 下常见的字体路径 (可选，防止 Docker 只有自定义字体不够用)
                fontFolders.Add("/usr/share/fonts");
                fontFolders.Add("/usr/local/share/fonts");
            }

            // 3. 应用字体设置
            // 第二个参数 true 表示递归扫描子文件夹
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

    /// <summary>
/// ✅ 架构师修正版: 采用等比缩放与严格 DPI 同步，解决 Save 崩溃问题
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
        

        // 2. 加载图像 (使用 using 确保资源释放)
        using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
        using (var srcPsd = (PsdImage)Aspose.PSD.Image.Load(assetPath))
        {
            float targetDpiX = (float)targetImage.HorizontalResolution;
            float targetDpiY = (float)targetImage.VerticalResolution;
            const double cmToInch = 1.0 / 2.54;

            // --- 核心修复 A: 计算逻辑改为“等比缩放适应容器” ---
            
            // 定义最大容器尺寸 (原逻辑中的固定尺寸作为 MaxBounds)
            int maxBoxWidth = Math.Max(1, (int)Math.Round(ICON_WIDTH_CM * cmToInch * targetDpiX));
            int maxBoxHeight = Math.Max(1, (int)Math.Round(ICON_HEIGHT_CM * cmToInch * targetDpiY));

            // 计算缩放比例 (参考你的测试代码逻辑)
            double scaleX = (double)maxBoxWidth / srcPsd.Width;
            double scaleY = (double)maxBoxHeight / srcPsd.Height;
            // 使用 Min 确保图片完全放入框内且不变形 (Contain模式)
            double scale = Math.Min(scaleX, scaleY); 

            // 计算最终实际像素尺寸
            int newWidth = Math.Max(1, (int)Math.Round(srcPsd.Width * scale));
            int newHeight = Math.Max(1, (int)Math.Round(srcPsd.Height * scale));

            // 3. 计算位置 (传入实际尺寸，确保居中逻辑正确)
            var pos = CalculateFixedIconPosition(targetImage, dim, newWidth, newHeight);
            int destLeft = pos.X;
            int destTop = pos.Y;

            // 4. 创建透明占位图层 (尺寸必须与即将置入的图完全一致)
            var placeholder = targetImage.AddRegularLayer();
            placeholder.DisplayName = "FixedIcon_Placeholder";
            placeholder.Left = destLeft;
            placeholder.Top = destTop;
            placeholder.Right = destLeft + newWidth;
            placeholder.Bottom = destTop + newHeight;

            // 填充透明像素 (初始化 Buffer，避免脏数据)
            var transparentPixels = new int[newWidth * newHeight];
            placeholder.SaveArgb32Pixels(new Aspose.PSD.Rectangle(0, 0, newWidth, newHeight), transparentPixels);

            // 5. 转换为智能对象
            var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "FixedIcon_SmartObject";

            // --- 核心修复 B: 预处理源图，确保“物理数据”与“显示意图”一致 ---

            // 调整源图尺寸 (使用高质量插值，避免锯齿)
            if (srcPsd.Width != newWidth || srcPsd.Height != newHeight)
            {
                srcPsd.Resize(newWidth, newHeight, ResizeType.LanczosResample);
            }
            
            // 强制同步 DPI (至关重要！避免 ReplaceContents 因 DPI 差异计算错误的变换矩阵)
            srcPsd.HorizontalResolution = targetDpiX;
            srcPsd.VerticalResolution = targetDpiY;

            // 6. 替换内容
            var resolution = new ResolutionSetting(targetDpiX, targetDpiY);
            smartLayer.ReplaceContents(srcPsd, resolution);

            // 7. 再次校准图层位置 (ReplaceContents 可能会重置部分属性，重新锁定位置)
            smartLayer.Left = destLeft;
            smartLayer.Top = destTop;
            // 注意：不要强制设置 Right/Bottom，让 SmartObject 根据 Content 自动计算，防止再次拉伸

            // 8. 保存 (使用 RLE 压缩，兼容性最好)
            var saveOptions = new PsdOptions
            {
                CompressionMethod = CompressionMethod.RLE,
                ColorMode = ColorModes.Rgb
            };
            
            // 这里 Save 应该就能成功了，因为内部数据结构现在是完美的 1:1 匹配
            targetImage.Save(tempOutputPath, saveOptions);
        }

        // 9. 原子性文件操作
        if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
        File.Move(tempOutputPath, targetPsdPath);
    }

    private void EmbedBarcodePdfAsSmartObject(string targetPsdPath, string pdfPath, PackagingDimensions dim)
    {
        if (!File.Exists(pdfPath) || !File.Exists(targetPsdPath)) return;

        string tempOutputPath = targetPsdPath + ".tmp";

        using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
        {
            using var pdfStream = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var pdfDoc = new AsposePdf.Document(pdfStream);

            float targetDpiX = (float)targetImage.HorizontalResolution;
            float targetDpiY = (float)targetImage.VerticalResolution;

            var pdfPage = pdfDoc.Pages[1];
            var pdfResolution = new AsposePdfDevices.Resolution((int)Math.Round(targetDpiX));
            var pngDevice = new AsposePdfDevices.PngDevice(pdfResolution);
            
            using var pageImageStream = new MemoryStream();
            pngDevice.Process(pdfPage, pageImageStream);
            pageImageStream.Position = 0;

            using var loadedImage = Aspose.PSD.Image.Load(pageImageStream);
            var raster = (RasterImage)loadedImage;
            raster.CacheData();

            const double cmToInch = 1.0 / 2.54;
            int targetWidthPx = (int)Math.Round(BARCODE_WIDTH_CM * cmToInch * targetDpiX);
            int targetHeightPx = (int)Math.Round(BARCODE_HEIGHT_CM * cmToInch * targetDpiY);

            // CreateScaledContainer 内部已经生成了全新的 PsdImage，所以它是“干净”的，不需要 Stream Wash
            using var srcPsd = CreateScaledContainer(raster, targetWidthPx, targetHeightPx, targetDpiX, targetDpiY);

            var pos = CalculateBarcodePosition(targetImage, dim, targetWidthPx, targetHeightPx);

            var placeholder = targetImage.AddRegularLayer();
            placeholder.DisplayName = "Barcode_Placeholder";
            placeholder.Left = pos.X;
            placeholder.Top = pos.Y;
            placeholder.Right = pos.X + targetWidthPx;
            placeholder.Bottom = pos.Y + targetHeightPx;

            var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "Barcode_SmartObject";
            
            var resolutionSetting = new ResolutionSetting(targetDpiX, targetDpiY);
            smartLayer.ReplaceContents(srcPsd, resolutionSetting);
            smartLayer.ContentsBounds = new Rectangle(0, 0, targetWidthPx, targetHeightPx);

            targetImage.Save(tempOutputPath, new PsdOptions { 
                CompressionMethod = CompressionMethod.RLE 
            });
        } 

        if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
        File.Move(tempOutputPath, targetPsdPath);
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

    private Point CalculateBarcodePosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);
        var C = CmToPixels(dim.InnerBleed);

        int backPanelX = A + (2 * Z) + X;
        int backPanelY = B + Z - (2 * C);
        int backPanelWidth = A + X;

        int panelCenterX = backPanelX + (backPanelWidth / 2);
        int destLeft = panelCenterX - (widthPx / 2);

        int bottomReserve = 100; 
        int visualBottomY = backPanelY + Y + (2 * C);
        int destTop = visualBottomY - bottomReserve - heightPx;

        return new Point(destLeft, destTop);
    }

    private Point CalculateFixedIconPosition(PsdImage psdImage, PackagingDimensions dim, int widthPx, int heightPx)
    {
        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);
        var C = CmToPixels(dim.InnerBleed);

        // 放在 Back Panel
        int backPanelX = A + (2 * Z) + X;
        int backPanelY = B + Z - (2 * C);
        int backPanelWidth = A + X;

        int centerX = backPanelX + (backPanelWidth / 2);
        int left = centerX - (widthPx / 2);

        // 垂直定位：位于条形码上方
        int bottomReserve = 100 + 450; 
        int visualBottomY = backPanelY + Y + (2 * C);
        int top = visualBottomY - bottomReserve - heightPx;

        return new Point(left, top);
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
            CreateRichTextLayer(psdImage, "Ingredients", "INGREDIENTS:", info.Ingredients,
                new Rectangle(startX + padding, currentY, textAreaWidth, 100), fontSize);
            currentY += 110;

            CreateRichTextLayer(psdImage, "Directions", "DIRECTIONS:", info.Directions,
                new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
            currentY += 90;

            CreateRichTextLayer(psdImage, "Warnings", "WARNINGS:", info.Warnings,
                new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
            currentY += 90;

            CreateRichTextLayer(psdImage, "Manufacturer_Back", "MANUFACTURER:", main.Manufacturer,
                new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
            currentY += 60;

            CreateRichTextLayer(psdImage, "Address_Back", "ADDRESS:", main.Address,
                new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
            currentY += 60;

            CreateRichTextLayer(psdImage, "Origin", "MADE IN:", info.Origin,
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