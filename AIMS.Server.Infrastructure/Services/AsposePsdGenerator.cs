using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Utils;
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.FillLayers;
using Aspose.PSD.FileFormats.Psd.Layers.FillSettings;
using Aspose.PSD.FileFormats.Psd.Layers.SmartObjects; // 必须引用
using Aspose.PSD.ImageOptions;
using Aspose.PSD.ProgressManagement;

// 显式指定 Aspose 类型以解决与 System.Drawing 的冲突
using Color = Aspose.PSD.Color;
using PointF = Aspose.PSD.PointF;
using Rectangle = Aspose.PSD.Rectangle;
using Point = Aspose.PSD.Point; // 解决 Point 歧义

// 引入 Aspose.Pdf 别名 (需引用 Aspose.PDF.dll)
using AsposePdf = Aspose.Pdf; 
using AsposePdfDevices = Aspose.Pdf.Devices;

namespace AIMS.Server.Infrastructure.Services;

public class AsposePsdGenerator : IPsdGenerator
{
    private const float DPI = 300f; // 核心要求：300 DPI
    
    // --- 条形码尺寸 (已放大) ---
    private const double BARCODE_WIDTH_CM = 6.0; 
    private const double BARCODE_HEIGHT_CM = 3.0; 

    // --- 固定图标尺寸 ---
    private const double ICON_WIDTH_CM = 5.0;
    private const double ICON_HEIGHT_CM = 2.8;

    // 复用 HttpClient 实例
    private static readonly HttpClient _httpClient = new HttpClient();

    public async Task<byte[]> GeneratePsdAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress = null)
    {
        return await Task.Run(async () => await GenerateInternalAsync(dim, assets, onProgress));
    }

    private async Task<byte[]> GenerateInternalAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress)
    {
        string? tempPsdPath = null;
        string? tempPdfPath = null;
        
        // 固定图标路径 (建议配置在 appsettings 或使用 IWebHostEnvironment 获取)
        // 这里假设它在运行目录下的 Assets 文件夹中
        string fixedIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "FixedIcon.psd");

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

            // 创建基础 PSD 的临时路径
            tempPsdPath = Path.GetTempFileName(); 

            // --- 阶段 2: 生成基础 PSD (包含结构和文本) ---
            onProgress?.Invoke(5, "正在创建基础图层...");
            
            using (var psdImage = new PsdImage(totalWidth, totalHeight))
            {
                psdImage.SetResolution(DPI, DPI);

                // 绘制刀版
                onProgress?.Invoke(10, "正在绘制刀版结构...");
                DrawStructureLayers(psdImage, X, Y, Z, A, B, C, onProgress);

                // 绘制辅助线
                onProgress?.Invoke(40, "正在生成智能辅助线...");
                AddGuidelines(psdImage, X, Y, Z, A, B, C);

                // 绘制文本
                onProgress?.Invoke(50, "正在渲染文本信息...");
                DrawInfoPanelAssets(psdImage, assets, dim);
                DrawMainPanelAssets(psdImage, assets, dim);

                // 保存基础版本到临时文件 (落盘)
                onProgress?.Invoke(70, "正在保存中间状态...");
                var saveOptions = new PsdOptions
                {
                    CompressionMethod = CompressionMethod.RLE,
                    ColorMode = ColorModes.Rgb
                };
                psdImage.Save(tempPsdPath, saveOptions);
            }
            // 此时 psdImage 已 Dispose，tempPsdPath 文件句柄已释放

            // --- 阶段 3: 处理条形码 (PDF -> Smart Object) ---
            if (assets.Images?.Barcode != null && !string.IsNullOrEmpty(assets.Images.Barcode.Url))
            {
                onProgress?.Invoke(75, "正在下载条形码...");
                
                var pdfBytes = await _httpClient.GetByteArrayAsync(assets.Images.Barcode.Url);
                if (pdfBytes.Length > 0)
                {
                    tempPdfPath = Path.GetTempFileName();
                    await File.WriteAllBytesAsync(tempPdfPath, pdfBytes);

                    onProgress?.Invoke(80, "正在置入条形码...");
                    // 传入 tempPsdPath，方法内部会处理 读->写->替换 的逻辑
                    EmbedBarcodePdfAsSmartObject(tempPsdPath, tempPdfPath, dim);
                }
            }

            // --- 阶段 4: 处理固定图标 (PSD -> Smart Object) ---
            if (File.Exists(fixedIconPath))
            {
                onProgress?.Invoke(90, "正在置入固定图标...");
                EmbedFixedAssetAsSmartObject(tempPsdPath, fixedIconPath, dim);
            }
            else
            {
                Console.WriteLine($"[Warning] 未找到固定图标文件: {fixedIconPath}");
            }

            // --- 阶段 5: 读取最终文件并返回 ---
            onProgress?.Invoke(95, "正在生成最终输出...");
            
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
            // 清理所有临时文件
            CleanupTempFile(tempPsdPath);
            CleanupTempFile(tempPdfPath);
            onProgress?.Invoke(100, "处理完成");
        }
    }

    /// <summary>
    /// 将本地 PDF 作为智能对象置入到目标 PSD 文件中
    /// 采用 Save到新临时文件 -> Dispose -> Replace 策略避免文件锁
    /// </summary>
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

            // --- PDF 转 PNG ---
            var pdfPage = pdfDoc.Pages[1];
            // 使用目标 DPI 渲染
            var pdfResolution = new AsposePdfDevices.Resolution((int)Math.Round(targetDpiX));
            var pngDevice = new AsposePdfDevices.PngDevice(pdfResolution);
            
            using var pageImageStream = new MemoryStream();
            pngDevice.Process(pdfPage, pageImageStream);
            pageImageStream.Position = 0;

            using var loadedImage = Aspose.PSD.Image.Load(pageImageStream);
            var raster = (RasterImage)loadedImage;
            raster.CacheData();

            // --- 计算尺寸 ---
            const double cmToInch = 1.0 / 2.54;
            int targetWidthPx = (int)Math.Round(BARCODE_WIDTH_CM * cmToInch * targetDpiX);
            int targetHeightPx = (int)Math.Round(BARCODE_HEIGHT_CM * cmToInch * targetDpiY);

            // --- 构建容器 PSD (Smart Object Content) ---
            // 使用辅助方法创建缩放后的容器，确保不变形
            using var srcPsd = CreateScaledContainer(raster, targetWidthPx, targetHeightPx, targetDpiX, targetDpiY);

            // --- 置入智能对象 ---
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

            // --- 保存到【新】临时路径 ---
            targetImage.Save(tempOutputPath, new PsdOptions { 
                CompressionMethod = CompressionMethod.RLE, 
                ColorMode = ColorModes.Rgb 
            });
        } 

        // --- 替换文件 ---
        if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
        File.Move(tempOutputPath, targetPsdPath);
    }

    /// <summary>
    /// 将本地 PSD 图标作为智能对象置入
    /// </summary>
    private void EmbedFixedAssetAsSmartObject(string targetPsdPath, string assetPath, PackagingDimensions dim)
    {
        if (!File.Exists(targetPsdPath) || !File.Exists(assetPath)) return;

        string tempOutputPath = targetPsdPath + ".tmp";

        using (var targetImage = (PsdImage)Aspose.PSD.Image.Load(targetPsdPath))
        {
            // 加载图标素材
            using var assetImage = (PsdImage)Aspose.PSD.Image.Load(assetPath);

            float targetDpiX = (float)targetImage.HorizontalResolution;
            float targetDpiY = (float)targetImage.VerticalResolution;

            // 计算目标像素尺寸 (5x2.8cm)
            const double cmToInch = 1.0 / 2.54;
            int targetWidthPx = (int)Math.Round(ICON_WIDTH_CM * cmToInch * targetDpiX);
            int targetHeightPx = (int)Math.Round(ICON_HEIGHT_CM * cmToInch * targetDpiY);

            // 计算位置
            var pos = CalculateFixedIconPosition(targetImage, dim, targetWidthPx, targetHeightPx);

            // 创建占位
            var placeholder = targetImage.AddRegularLayer();
            placeholder.DisplayName = "Icon_Placeholder";
            placeholder.Left = pos.X;
            placeholder.Top = pos.Y;
            placeholder.Right = pos.X + targetWidthPx;
            placeholder.Bottom = pos.Y + targetHeightPx;

            // 转智能对象
            var smartLayer = targetImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "Fixed_Icon_SmartObject";

            // 创建缩放容器并注入
            using (var containerPsd = CreateScaledContainer(assetImage, targetWidthPx, targetHeightPx, targetDpiX, targetDpiY))
            {
                var resolutionSetting = new ResolutionSetting(targetDpiX, targetDpiY);
                smartLayer.ReplaceContents(containerPsd, resolutionSetting);
            }
            
            smartLayer.ContentsBounds = new Rectangle(0, 0, targetWidthPx, targetHeightPx);

            // 保存
            targetImage.Save(tempOutputPath, new PsdOptions { CompressionMethod = CompressionMethod.RLE, ColorMode = ColorModes.Rgb });
        }

        // 替换
        if (File.Exists(targetPsdPath)) File.Delete(targetPsdPath);
        File.Move(tempOutputPath, targetPsdPath);
    }

    /// <summary>
    /// 辅助方法：创建一个标准尺寸的 PSD 容器，并将源图等比缩放居中放入
    /// </summary>
    private PsdImage CreateScaledContainer(RasterImage source, int width, int height, float dpiX, float dpiY)
    {
        var container = new PsdImage(width, height);
        container.SetResolution(dpiX, dpiY);
        container.ColorMode = ColorModes.Rgb;

        // 计算缩放比例 (Fit Center)
        double scaleX = (double)width / source.Width;
        double scaleY = (double)height / source.Height;
        double scale = Math.Min(scaleX, scaleY); 

        int newW = Math.Max(1, (int)(source.Width * scale));
        int newH = Math.Max(1, (int)(source.Height * scale));

        // 缩放源图
        if (source.Width != newW || source.Height != newH)
        {
            source.Resize(newW, newH, ResizeType.LanczosResample);
        }

        // 创建图层并绘制
        var layer = container.AddRegularLayer();
        layer.Left = 0; layer.Top = 0; layer.Right = width; layer.Bottom = height;
        
        // 居中计算
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

        // Back 面板区域
        int backPanelX = A + (2 * Z) + X;
        int backPanelY = B + Z - (2 * C);
        int backPanelWidth = A + X;

        // 水平居中
        int panelCenterX = backPanelX + (backPanelWidth / 2);
        int destLeft = panelCenterX - (widthPx / 2);

        // 垂直定位：位于背面底部上方
        // 如果条形码太大，可以适当减小 reserve
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

        // 垂直定位：比条形码更高一点
        // 假设条形码预留 100 + 条形码高度(约350px)，我们再往上预留一些
        int bottomReserve = 100 + 450; 
        int visualBottomY = backPanelY + Y + (2 * C);
        int top = visualBottomY - bottomReserve - heightPx;

        return new Point(left, top);
    }

    private void DrawStructureLayers(PsdImage psdImage, int X, int Y, int Z, int A, int B, int C, Action<int, string>? onProgress)
    {
        // 背景
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

            if (!string.IsNullOrEmpty(label))
            {
                var labelPortion = textData.Items[0];
                labelPortion.Text = label + " ";
                labelPortion.Style.FauxBold = true;
                labelPortion.Style.FontName = "Arial";
                labelPortion.Style.FillColor = Color.Black;
                
                var contentPortion = textData.ProducePortion();
                contentPortion.Text = content;
                contentPortion.Style.FontSize = fontSizePixels;
                contentPortion.Style.FontName = "Arial";
                contentPortion.Style.FauxBold = false;
                contentPortion.Style.FillColor = Color.Black;
                textData.AddPortion(contentPortion);
            }
            else
            {
                var portion = textData.Items[0];
                portion.Text = content;
                portion.Style.FontName = "Arial";
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