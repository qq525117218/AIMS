using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Utils;
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.FillLayers;
using Aspose.PSD.FileFormats.Psd.Layers.FillSettings;
using Aspose.PSD.ImageOptions;
using Aspose.PSD.ProgressManagement;


namespace AIMS.Server.Infrastructure.Services;

public class AsposePsdGenerator : IPsdGenerator
{
    private const float DPI = 300f; // 核心要求：300 DPI
    
    // 复用 HttpClient 实例
    private static readonly HttpClient _httpClient = new HttpClient();

    public async Task<byte[]> GeneratePsdAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress = null)
    {
        return await Task.Run(async () => await GenerateInternalAsync(dim, assets, onProgress));
    }

    private async Task<byte[]> GenerateInternalAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress)
    {
        try
        {
            // --- 阶段 1: 初始化 (0% - 5%) ---
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

            onProgress?.Invoke(5, "画布尺寸计算完成，正在创建图像...");

            using (var psdImage = new PsdImage(totalWidth, totalHeight))
            {
                psdImage.SetResolution(DPI, DPI);

                // --- 阶段 2: 绘制刀版结构 (5% - 40%) ---
                onProgress?.Invoke(10, "正在绘制刀版结构...");
                DrawStructureLayers(psdImage, X, Y, Z, A, B, C, onProgress);

                // --- 阶段 3: 绘制内容与辅助线 (40% - 80%) ---
                onProgress?.Invoke(40, "正在生成智能辅助线...");
                AddGuidelines(psdImage, X, Y, Z, A, B, C);

                onProgress?.Invoke(50, "正在渲染文本信息...");
                DrawInfoPanelAssets(psdImage, assets, dim);
                DrawMainPanelAssets(psdImage, assets, dim);

                // 处理条形码 (下载 PDF -> 转 PNG -> 置入智能对象)
                if (assets.Images?.Barcode != null && !string.IsNullOrEmpty(assets.Images.Barcode.Url))
                {
                    onProgress?.Invoke(65, "正在处理条形码 (PDF转换中)...");
                    await DrawBarcodeSmartObjectAsync(psdImage, assets.Images.Barcode.Url, dim);
                }

                onProgress?.Invoke(80, "渲染完成，准备保存...");

                // --- 阶段 4: 保存文件 (80% - 100%) ---
                using (var ms = new MemoryStream())
                {
                    var saveOptions = new PsdOptions
                    {
                        CompressionMethod = CompressionMethod.RLE,
                        ColorMode = ColorModes.Rgb,
                        ProgressEventHandler = (ProgressEventHandlerInfo info) =>
                        {
                            if (info.MaxValue > 0)
                            {
                                int saveProgress = 80 + (int)((info.Value / (double)info.MaxValue) * 20);
                                onProgress?.Invoke(saveProgress, "正在生成文件流...");
                            }
                        }
                    };

                    psdImage.Save(ms, saveOptions);

                    onProgress?.Invoke(100, "生成完成");
                    return ms.ToArray();
                }
            }
        }
        catch (Exception)
        {
            onProgress?.Invoke(0, "生成失败");
            throw;
        }
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

   /// <summary>
    /// 使用 Aspose.Words 渲染 PDF -> 创建容器 PSD -> 智能对象置入
    /// (不依赖 Aspose.PDF)
    /// </summary>
   /// <summary>
    /// 下载 PDF -> 使用 Aspose.Words 转 PNG -> 创建容器 PSD -> 智能对象置换
    /// (严格参考 Place_LocalPdf_As_SmartObject_Into_TargetPsd_Fact 逻辑)
    /// </summary>
    private async Task DrawBarcodeSmartObjectAsync(PsdImage psdImage, string pdfUrl, PackagingDimensions dim)
    {
        try
        {
            // 1. 下载 PDF 文件
            Console.WriteLine($"[PsdGenerator] 正在下载条形码 PDF: {pdfUrl}");
            var fileBytes = await _httpClient.GetByteArrayAsync(pdfUrl);

            if (fileBytes == null || fileBytes.Length == 0)
            {
                Console.WriteLine("[PsdGenerator] 错误: 下载的 PDF 文件为空");
                return;
            }

            // 2. 使用 Aspose.Words 将 PDF 渲染为 PNG
            byte[] pngBytes;
            try
            {
                using var pdfStream = new MemoryStream(fileBytes);
                var wordDoc = new Aspose.Words.Document(pdfStream);

                var saveOptions = new Aspose.Words.Saving.ImageSaveOptions(Aspose.Words.SaveFormat.Png)
                {
                    Resolution = DPI, // 300 DPI
                    PageSet = new Aspose.Words.Saving.PageSet(0) // 仅第一页
                };

                using var pngMs = new MemoryStream();
                wordDoc.Save(pngMs, saveOptions);
                pngBytes = pngMs.ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PsdGenerator] Aspose.Words 转换 PDF 失败: {ex.Message}");
                return;
            }

            if (pngBytes == null || pngBytes.Length == 0)
            {
                Console.WriteLine("[PsdGenerator] 错误: 转换后的 PNG 数据为空");
                return;
            }

            // 3. 加载 PNG (关键：确保流位置正确)
            using var pngStream = new MemoryStream(pngBytes);
            using var raster = (Aspose.PSD.RasterImage)Aspose.PSD.Image.Load(pngStream);
            raster.CacheData(); // 缓存数据以加速读取

            // --- 4. 准备智能对象的内容容器 (Inner PSD) ---
            // 定义条形码目标尺寸 (例如 3.5cm x 2.0cm)
            double targetWidthCm = 3.5;
            double targetHeightCm = 2.0;

            const double cmToInch = 1.0 / 2.54;
            int targetWidthPx = (int)Math.Round(targetWidthCm * cmToInch * DPI);
            int targetHeightPx = (int)Math.Round(targetHeightCm * cmToInch * DPI);

            // 创建容器 PSD (作为智能对象的源)
            using var srcPsd = new PsdImage(targetWidthPx, targetHeightPx);
            srcPsd.SetResolution(DPI, DPI);
            srcPsd.ColorMode = ColorModes.Rgb; // 确保内部使用 RGB

            // 在容器中添加图层
            var srcLayer = srcPsd.AddRegularLayer();
            srcLayer.DisplayName = "Barcode_Content";
            // 确保图层填满容器
            srcLayer.Left = 0; srcLayer.Top = 0;
            srcLayer.Right = targetWidthPx; srcLayer.Bottom = targetHeightPx;

            // --- 计算缩放并绘制 (保持等比缩放并居中) ---
            double fitScaleX = (double)targetWidthPx / raster.Width;
            double fitScaleY = (double)targetHeightPx / raster.Height;
            double fitScale = Math.Min(fitScaleX, fitScaleY);

            int fitWidth = Math.Max(1, (int)Math.Round(raster.Width * fitScale));
            int fitHeight = Math.Max(1, (int)Math.Round(raster.Height * fitScale));

            // 缩放原始 PNG 像素
            if (raster.Width != fitWidth || raster.Height != fitHeight)
            {
                raster.Resize(fitWidth, fitHeight);
            }

            // 居中计算
            int imgLeft = (targetWidthPx - fitWidth) / 2;
            int imgTop = (targetHeightPx - fitHeight) / 2;

            var srcRect = new Aspose.PSD.Rectangle(0, 0, fitWidth, fitHeight);
            var destRect = new Aspose.PSD.Rectangle(imgLeft, imgTop, fitWidth, fitHeight);

            // 将 PNG 像素写入容器图层
            var pixels = raster.LoadArgb32Pixels(srcRect);
            srcLayer.SaveArgb32Pixels(destRect, pixels);

            // --- 5. 在主画布 (Target PSD) 中创建并放置智能对象 ---

            // 计算在主画布背面 (Back Panel) 的位置
            var X = CmToPixels(dim.Length);
            var Y = CmToPixels(dim.Height);
            var Z = CmToPixels(dim.Width);
            var A = CmToPixels(dim.BleedLeftRight);
            var B = CmToPixels(dim.BleedTopBottom);
            var C = CmToPixels(dim.InnerBleed);

            int backPanelX = A + (2 * Z) + X;
            int backPanelY = B + Z - (2 * C);
            int backPanelWidth = A + X;

            // 水平居中
            int panelCenterX = backPanelX + (backPanelWidth / 2);
            int destLeft = panelCenterX - (targetWidthPx / 2);

            // 垂直定位：位于背面底部上方 (预留 130px 给 CapacityInfo)
            int bottomReserve = 130;
            int visualBottomY = backPanelY + Y + (2 * C);
            int destTop = visualBottomY - bottomReserve - targetHeightPx;

            // A. 创建占位图层 (Placeholder)
            // 使用 AddRegularLayer 而不是 new Layer() 以避免部分初始化问题
            var placeholder = psdImage.AddRegularLayer();
            placeholder.DisplayName = "Barcode_Placeholder";
            placeholder.Left = destLeft;
            placeholder.Top = destTop;
            placeholder.Right = destLeft + targetWidthPx;
            placeholder.Bottom = destTop + targetHeightPx;

            // B. 转换为智能对象 (ConvertToSmartObject)
            // 这一步会根据 placeholder 创建一个新的 SmartObjectLayer
            var smartLayer = psdImage.SmartObjectProvider.ConvertToSmartObject(new[] { placeholder });
            smartLayer.DisplayName = "Barcode_SmartObject";

            // C. 替换内容 (ReplaceContents)
            // 将我们做好的 srcPsd (容器) 注入进去
            var resolutionSetting = new ResolutionSetting(DPI, DPI);
            smartLayer.ReplaceContents(srcPsd, resolutionSetting);

            // D. 显式设置边界 (防止显示变形)
            smartLayer.ContentsBounds = new Aspose.PSD.Rectangle(0, 0, targetWidthPx, targetHeightPx);

            Console.WriteLine("[PsdGenerator] 条形码智能对象置入成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PsdGenerator] Error placing barcode smart object: {ex.Message}");
            // 不抛出异常，以免阻断整个 PSD 生成（条形码缺失总比生成失败好）
        }
    }

    /// <summary>
    /// 辅助方法：统一计算并设置图层位置 (支持 Layer 和 SmartObjectLayer)
    /// </summary>
    private void PositionLayer(Layer layer, PackagingDimensions dim, int X, int Y, int Z, int A, int B, int C)
    {
        // Back 面板区域
        int backPanelX = A + (2 * Z) + X;
        int backPanelY = B + Z - (2 * C);
        int backPanelWidth = A + X;

        // 居中计算
        int currentWidth = layer.Width;
        int currentHeight = layer.Height;

        int panelCenterX = backPanelX + (backPanelWidth / 2);
        int targetX = panelCenterX - (currentWidth / 2);

        // 底部定位：预留 150px (CapacityInfo + Padding)
        int bottomReserve = 150;
        int visualBottomY = backPanelY + Y + (2 * C);
        int targetY = visualBottomY - bottomReserve - currentHeight;

        layer.Left = targetX;
        layer.Top = targetY;
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
        int textRowHeight = 60;

        if (!string.IsNullOrWhiteSpace(main.CapacityInfo))
        {
            currentBottomY -= capacityHeight;
            var rect = new Rectangle(contentStartX, currentBottomY, contentWidth, capacityHeight);
            CreateRichTextLayer(psdImage, "CapacityInfo_Front", "", main.CapacityInfo, rect, 10f);
        }

        /*if (!string.IsNullOrWhiteSpace(main.Address))
        {
            currentBottomY -= textRowHeight;
            currentBottomY -= 10; 
            CreateRichTextLayer(psdImage, "MainAddress", "", main.Address, 
                new Rectangle(contentStartX, currentBottomY, contentWidth, textRowHeight), 6f);
        }

        if (!string.IsNullOrWhiteSpace(main.Manufacturer))
        {
            currentBottomY -= textRowHeight;
            currentBottomY -= 10; 
            CreateRichTextLayer(psdImage, "MainManufacturer", "", main.Manufacturer, 
                new Rectangle(contentStartX, currentBottomY, contentWidth, textRowHeight), 6f);
        }*/
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
}