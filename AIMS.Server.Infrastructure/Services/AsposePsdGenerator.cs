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

    /// <summary>
    /// 生成 PSD 文件 (支持进度回调)
    /// </summary>
    /// <param name="dim">物理规格</param>
    /// <param name="assets">视觉素材</param>
    /// <param name="onProgress">进度回调 (0-100, 状态描述)</param>
    /// <returns>PSD 文件字节流</returns>
    public async Task<byte[]> GeneratePsdAsync(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress = null)
    {
        // 将生成任务放入后台线程，避免阻塞
        return await Task.Run(() => GenerateInternal(dim, assets, onProgress));
    }

    /// <summary>
    /// 核心生成逻辑
    /// </summary>
    private byte[] GenerateInternal(PackagingDimensions dim, PackagingAssets assets, Action<int, string>? onProgress)
    {
        try
        {
            // --- 阶段 1: 初始化 (0% - 5%) ---
            onProgress?.Invoke(1, "正在初始化画布参数...");

            // 1. 基础像素转换 (CM -> Pixels)
            var X = CmToPixels(dim.Length); // 长
            var Y = CmToPixels(dim.Height); // 高
            var Z = CmToPixels(dim.Width);  // 宽

            var A = CmToPixels(dim.BleedLeftRight); // 左右出血
            var B = CmToPixels(dim.BleedTopBottom); // 上下出血
            var C = CmToPixels(dim.InnerBleed);     // 内出血

            // 2. 计算画布总尺寸
            var totalWidth = (2 * X) + (2 * Z) + (2 * A);

            // 高度计算
            var calculatedHeight = Y + (2 * Z) + (2 * B) - (4 * C);
            var minRequiredHeight = B + (2 * Z) + Y;
            var totalHeight = Math.Max(calculatedHeight, minRequiredHeight);

            onProgress?.Invoke(5, "画布尺寸计算完成，正在创建图像...");

            // 4. 创建并绘制 PSD
            using (var psdImage = new PsdImage(totalWidth, totalHeight))
            {
                psdImage.SetResolution(DPI, DPI);

                // --- 阶段 2: 绘制刀版结构 (5% - 40%) ---
                onProgress?.Invoke(10, "正在绘制刀版结构...");

                // BG (背景/整体轮廓)
                CreateShapeLayer(psdImage, "BG",
                    width: (2 * X) + (2 * Z) + (2 * A),
                    height: Y + (4 * C),
                    x: 0,
                    y: B + Z - (2 * C),
                    Color.White);

                onProgress?.Invoke(15, "正在绘制侧面板...");

                // Left (左侧面)
                CreateShapeLayer(psdImage, "left",
                    width: A + X,
                    height: Y + (4 * C),
                    x: 0,
                    y: B + Z - (2 * C),
                    Color.White);

                // Front (正面)
                CreateShapeLayer(psdImage, "front",
                    width: X,
                    height: Y + (4 * C),
                    x: A + Z,
                    y: B + Z - (2 * C),
                    Color.White);

                onProgress?.Invoke(25, "正在绘制主面板...");

                // Right (右侧面)
                CreateShapeLayer(psdImage, "right",
                    width: X,
                    height: Y + (4 * C),
                    x: A + Z + X,
                    y: B + Z - (2 * C),
                    Color.White);

                // Back (背面)
                CreateShapeLayer(psdImage, "back",
                    width: A + X,
                    height: Y + (4 * C),
                    x: A + (2 * Z) + X,
                    y: B + Z - (2 * C),
                    Color.White);

                onProgress?.Invoke(35, "正在绘制顶底盖...");

                // Top (顶盖)
                CreateShapeLayer(psdImage, "top",
                    width: X,
                    height: Z,
                    x: A + Z,
                    y: B,
                    Color.White);

                // Bottom (底盖)
                CreateShapeLayer(psdImage, "bottom",
                    width: X,
                    height: Z,
                    x: A + (2 * Z) + X,
                    y: B + Z + Y,
                    Color.White);

                // --- 阶段 3: 绘制内容与辅助线 (40% - 70%) ---
                onProgress?.Invoke(40, "正在生成智能辅助线...");

                // 辅助线组
                AddGuidelines(psdImage, X, Y, Z, A, B, C);

                onProgress?.Invoke(55, "正在渲染动态文本和条码...");

                // 绘制信息面板内容
                DrawInfoPanelAssets(psdImage, assets, dim);

                onProgress?.Invoke(70, "图层渲染完成，准备压缩保存...");

                // --- 阶段 4: 保存文件 (70% - 100%) ---
                // 5. 保存到内存流返回
                using (var ms = new MemoryStream())
                {
                    var saveOptions = new PsdOptions
                    {
                        CompressionMethod = CompressionMethod.RLE,
                        ColorMode = ColorModes.Rgb,
                        // ✅ 修正：使用 ProgressEventHandlerInfo
                        ProgressEventHandler = (ProgressEventHandlerInfo info) =>
                        {
                            // 简单的防止除以零检查
                            if (info.MaxValue > 0)
                            {
                                // 将 Aspose 的保存进度 (0-100) 映射到总任务进度的 (70-100)
                                int saveProgress = 70 + (int)((info.Value / (double)info.MaxValue) * 30);
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

    // --- 以下私有方法保持原有逻辑不变 ---

    private void AddGuidelines(PsdImage psdImage, int X, int Y, int Z, int A, int B, int C)
    {
        var horizontalWidth = 2 * X + 2 * Z + 2 * A;
        var verticalHeight = Y + 2 * Z + 2 * B - 4 * C;

        var topIndex = psdImage.Layers.Length;
        var lineGroup = psdImage.AddLayerGroup("GuidelineGroup", topIndex, false);

        // Y轴辅助线
        CreateGuidelineLayer(psdImage, "guideline_Y001", 0, B, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y002", 0, B + Z - 2 * C, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y003", 0, B + Z, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y004", 0, B + Z + Y, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y005", 0, B + Z + Y + 2 * C, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y006", 0, B + Z + Y + Z, horizontalWidth, 1, lineGroup);

        // X轴辅助线
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
        catch (Exception ex)
        {
            Console.WriteLine($"CreateWhiteRectangleLineLayer exception: {ex.Message}");
        }
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
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating layer {layerName}: {ex.Message}");
        }
    }

    private void DrawInfoPanelAssets(PsdImage psdImage, PackagingAssets assets, PackagingDimensions dim)
    {
        var info = assets.Texts.InfoPanel;
        if (info == null) return;

        var X = CmToPixels(dim.Length);
        var Y = CmToPixels(dim.Height);
        var Z = CmToPixels(dim.Width);
        var A = CmToPixels(dim.BleedLeftRight);
        var B = CmToPixels(dim.BleedTopBottom);
        var C = CmToPixels(dim.InnerBleed);

        // Back 面板的左上角坐标
        int startX = A + (2 * Z) + X;
        int startY = B + Z - (2 * C);
        int panelWidth = A + X; // 背板宽度

        int padding = 30;
        int currentY = startY + padding;
        int textAreaWidth = panelWidth - (2 * padding);

        float fontSize = 6f;

        // --- 依次生成图层 ---
        CreateRichTextLayer(psdImage, "Ingredients", "INGREDIENTS:", info.Ingredients,
            new Rectangle(startX + padding, currentY, textAreaWidth, 100), fontSize);
        currentY += 110;

        CreateRichTextLayer(psdImage, "Directions", "DIRECTIONS:", info.Directions,
            new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
        currentY += 90;

        CreateRichTextLayer(psdImage, "Warnings", "WARNINGS:", info.Warnings,
            new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
        currentY += 90;

        CreateRichTextLayer(psdImage, "Manufacturer", "MANUFACTURER:", info.Manufacturer,
            new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
        currentY += 60;

        CreateRichTextLayer(psdImage, "Address", "ADDRESS:", info.Address,
            new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
        currentY += 60;

        CreateRichTextLayer(psdImage, "Origin", "MADE IN:", info.Origin,
            new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
    }

    private void CreateRichTextLayer(PsdImage psdImage, string layerName, string label, string content, Rectangle rect, float fontSizePt)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        // 1. 创建文本图层
        var textLayer = psdImage.AddTextLayer(label + " ", rect);
        textLayer.DisplayName = layerName;

        // 2. 获取文本数据接口
        var textData = textLayer.TextData;

        float fontSizePixels = PtToPixels(fontSizePt);
        
        // 标题片段 (加粗)
        var labelPortion = textData.Items[0];
        labelPortion.Style.FontName = "Arial";
        labelPortion.Style.FontSize = fontSizePixels;
        labelPortion.Style.FauxBold = true;
        labelPortion.Style.FillColor = Color.Black;
        labelPortion.Paragraph.Justification = JustificationMode.Left;

        // 内容片段 (常规)
        var contentPortion = textData.ProducePortion();
        contentPortion.Text = content;
        contentPortion.Style.FontName = "Arial";
        contentPortion.Style.FontSize = fontSizePixels;
        contentPortion.Style.FauxBold = false;
        contentPortion.Style.FillColor = Color.Black;
        contentPortion.Paragraph.Justification = JustificationMode.Left;

        textData.AddPortion(contentPortion);

        // 3. 应用更改
        textData.UpdateLayerData();
    }

    private int CmToPixels(double cm)
    {
        return (int)Math.Round((cm / 2.54) * DPI);
    }

    private float PtToPixels(float pt)
    {
        return (pt * DPI) / 72f;
    }
}