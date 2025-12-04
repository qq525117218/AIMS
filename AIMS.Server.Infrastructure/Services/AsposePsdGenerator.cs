using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Utils;
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.FillLayers;
using Aspose.PSD.FileFormats.Psd.Layers.FillSettings;
using Aspose.PSD.ImageOptions;
using Aspose.PSD.Sources;

using Aspose.PSD.FileFormats.Psd.Layers.Text;

namespace AIMS.Server.Infrastructure.Services;

public class AsposePsdGenerator : IPsdGenerator
{
    private const float DPI = 300f; // 核心要求：300 DPI

    // ✅ 修改签名：接收 assets，但目前 logic 不处理它
    public async Task<byte[]> GeneratePsdAsync(PackagingDimensions dim, PackagingAssets assets)
    {
        // 即使 assets 传进来，目前我们也只用 dim 生成刀版和辅助线
        return await Task.Run(() => GenerateInternal(dim, assets));
    }

    /// <summary>
    /// 核心生成逻辑
    /// </summary>
    private byte[] GenerateInternal(PackagingDimensions dim, PackagingAssets assets)
    {
        // 1. 基础像素转换 (CM -> Pixels)
        var X = CmToPixels(dim.Length); // 长
        var Y = CmToPixels(dim.Height); // 高
        var Z = CmToPixels(dim.Width);  // 宽

        var A = CmToPixels(dim.BleedLeftRight); // 左右出血
        var B = CmToPixels(dim.BleedTopBottom); // 上下出血
        var C = CmToPixels(dim.InnerBleed);     // 内出血

        // 2. 计算画布总尺寸
        var totalWidth = (2 * X) + (2 * Z) + (2 * A);
        
        // 高度计算 (保持原有逻辑)
        var calculatedHeight = Y + (2 * Z) + (2 * B) - (4 * C);
        var minRequiredHeight = B + (2 * Z) + Y;
        var totalHeight = Math.Max(calculatedHeight, minRequiredHeight);

        // 3. 配置 PSD 选项
        var psdOptions = new PsdOptions
        {
            Source = new StreamSource(new MemoryStream()),
            ColorMode = ColorModes.Rgb,
            ChannelsCount = 3,
            ChannelBitsCount = 8,
            ResolutionSettings = new ResolutionSetting(DPI, DPI)
        };

        // 4. 创建并绘制 PSD
        using (var psdImage = new PsdImage(totalWidth, totalHeight))
        {
            psdImage.SetResolution(DPI, DPI);

            // --- 绘制各个面板 (标准图层) ---
            // 逻辑完全保留，不引用 assets

            // BG (背景/整体轮廓)
            CreateShapeLayer(psdImage, "BG",
                width: (2 * X) + (2 * Z) + (2 * A),
                height: Y + (4 * C),
                x: 0,
                y: B + Z - (2 * C),
                Color.White);

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

            // --- 辅助线组 ---
            AddGuidelines(psdImage, X, Y, Z, A, B, C);
            
            DrawInfoPanelAssets(psdImage, assets, dim);

            // 5. 保存到内存流返回
            using (var ms = new MemoryStream())
            {
                var saveOptions = new PsdOptions
                {
                    CompressionMethod = CompressionMethod.RLE,
                    ColorMode = ColorModes.Rgb
                };
                psdImage.Save(ms, saveOptions);
                return ms.ToArray();
            }
        }
    }

    // --- 以下私有方法保持完全不变 ---

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
    
    
    /// <summary>
/// 绘制信息面板的所有文本资产
/// </summary>
private void DrawInfoPanelAssets(PsdImage psdImage, PackagingAssets assets, PackagingDimensions dim)
{
    var info = assets.Texts.InfoPanel;
    if (info == null) return;

    // --- 布局参数计算 (模拟定位在背板 Back Panel) ---
    // 你需要根据实际刀版逻辑获取 Back 面板的坐标
    // 假设：X, Y, Z, A, B, C 已经在上层计算好，或者通过参数传进来
    // 这里为了演示，我们重新简单计算一下 Back 面板的起始点 (参考 GenerateInternal 中的逻辑)
    
    var X = CmToPixels(dim.Length);
    var Y = CmToPixels(dim.Height);
    var Z = CmToPixels(dim.Width);
    var A = CmToPixels(dim.BleedLeftRight);
    var B = CmToPixels(dim.BleedTopBottom);
    var C = CmToPixels(dim.InnerBleed);

    // Back 面板的左上角坐标 (参考 CreateShapeLayer("back", ...))
    int startX = A + (2 * Z) + X; 
    int startY = B + Z - (2 * C);
    int panelWidth = A + X; // 背板宽度

    // 定义文本区域的内边距 (Padding)
    int padding = 30; 
    int currentY = startY + padding;
    int textAreaWidth = panelWidth - (2 * padding);
    int lineHeight = 50; // 这里的行高是预估的，如果文本很长需要自动换行计算高度

    // 字号 6pt
    float fontSize = 6f;

    // --- 依次生成图层 ---

    // 1. Ingredients (成分)
    // 假设文本框高度为 100 像素
    CreateRichTextLayer(psdImage, "Ingredients", "INGREDIENTS:", info.Ingredients, 
        new Rectangle(startX + padding, currentY, textAreaWidth, 100), fontSize);
    currentY += 110; // 移动 Y 轴

    // 2. Directions (使用方法) - 你要求的例子
    CreateRichTextLayer(psdImage, "Directions", "DIRECTIONS:", info.Directions, 
        new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
    currentY += 90;

    // 3. Warnings (警告)
    CreateRichTextLayer(psdImage, "Warnings", "WARNINGS:", info.Warnings, 
        new Rectangle(startX + padding, currentY, textAreaWidth, 80), fontSize);
    currentY += 90;

    // 4. Manufacturer (制造商)
    CreateRichTextLayer(psdImage, "Manufacturer", "MANUFACTURER:", info.Manufacturer, 
        new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
    currentY += 60;

    // 5. Address (地址)
    CreateRichTextLayer(psdImage, "Address", "ADDRESS:", info.Address, 
        new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
    currentY += 60;

    // 6. Origin (原产地)
    CreateRichTextLayer(psdImage, "Origin", "MADE IN:", info.Origin, 
        new Rectangle(startX + padding, currentY, textAreaWidth, 50), fontSize);
}
    
    /// <summary>
    /// 创建富文本图层（支持 标题加粗 + 内容常规 的组合样式）
    /// </summary>
    /// <param name="psdImage">PSD 对象</param>
    /// <param name="layerName">图层名称</param>
    /// <param name="label">标题（将加粗），如 "Directions:"</param>
    /// <param name="content">内容（常规），如 "Cleanse and dry..."</param>
    /// <param name="rect">文本框区域</param>
    /// <param name="fontSizePt">字号（点）</param>
    /// <summary>
    /// 创建富文本图层（支持 标题加粗 + 内容常规 的组合样式）
    /// </summary>
    private void CreateRichTextLayer(PsdImage psdImage, string layerName, string label, string content, Rectangle rect, float fontSizePt)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        // 1. 创建文本图层
        // 初始化时先只放入标题和空格，Aspose 会自动生成第一个 Portion (Items[0])
        var textLayer = psdImage.AddTextLayer(label + " ", rect);
        textLayer.DisplayName = layerName;

        // 2. 获取文本数据接口
        var textData = textLayer.TextData;

        float fontSizePixels = PtToPixels(fontSizePt);
        // TextData.Items[0] 是默认生成的第一个文本片段
        var labelPortion = textData.Items[0];
        labelPortion.Style.FontName = "Arial";
        labelPortion.Style.FontSize = fontSizePixels;
        labelPortion.Style.FauxBold = true; // 加粗
        labelPortion.Style.FillColor = Color.Black;
        // [修复] 枚举类型是 JustificationMode，属性是 Justification
        labelPortion.Paragraph.Justification = JustificationMode.Left; 

        // --- 添加第二个片段 (内容: Cleanse and dry...) ---
        // 使用 ProducePortion 创建新片段
        var contentPortion = textData.ProducePortion();
        contentPortion.Text = content;
        contentPortion.Style.FontName = "Arial";
        contentPortion.Style.FontSize = fontSizePixels;
        contentPortion.Style.FauxBold = false; // 不加粗
        contentPortion.Style.FillColor = Color.Black;
        contentPortion.Paragraph.Justification = JustificationMode.Left;

        // [修复] 使用 AddPortion 将片段加入文本数据
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