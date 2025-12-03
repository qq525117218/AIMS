using System.Net.Mime;
using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using AIMS.Server.Infrastructure.Utils; // 确保包含 BezierKnot, PathShapeGen, VectorDataProvider
using Aspose.PSD;
using Aspose.PSD.FileFormats.Psd;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.FillLayers;
using Aspose.PSD.FileFormats.Psd.Layers.FillSettings;
using Aspose.PSD.ImageOptions;
using Aspose.PSD.Sources;

namespace AIMS.Server.Infrastructure.Services;

public class AsposePsdGenerator : IPsdGenerator
{
    private const float DPI = 300f; // 核心要求：300 DPI

    public async Task<byte[]> GeneratePsdAsync(PackagingDimensions dim)
    {
        // 将逻辑封装到 GenerateInternal，便于管理和调试
        // 这里保留 Task.Run 以避免阻塞主线程（但在高并发下建议由外部队列调度）
        return await Task.Run(() => GenerateInternal(dim));
    }

    /// <summary>
    /// 核心生成逻辑
    /// </summary>
    private byte[] GenerateInternal(PackagingDimensions dim)
    {
        // 1. 基础像素转换 (CM -> Pixels)
        // L=Length(长), H=Height(高), W=Width(宽)
        var X = CmToPixels(dim.Length); // 长
        var Y = CmToPixels(dim.Height); // 高
        var Z = CmToPixels(dim.Width); // 宽

        var A = CmToPixels(dim.BleedLeftRight); // 左右出血
        var B = CmToPixels(dim.BleedTopBottom); // 上下出血
        var C = CmToPixels(dim.InnerBleed); // 内出血

        // 2. 计算画布总尺寸
        // 宽度: 2*X + 2*Z + 2*A
        var totalWidth = (2 * X) + (2 * Z) + (2 * A);

        // 高度: Y + 2*Z + 2*B - 4*C
        var calculatedHeight = Y + (2 * Z) + (2 * B) - (4 * C);

        // [健壮性修复] 防止画布被裁剪
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

            // --- ✅ 新增：添加辅助线组 ---
            AddGuidelines(psdImage, X, Y, Z, A, B, C);
            

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

    /// <summary>
    /// 添加辅助线组
    /// </summary>
    private void AddGuidelines(PsdImage psdImage, int X, int Y, int Z, int A, int B, int C)
    {
        var horizontalWidth = 2 * X + 2 * Z + 2 * A;
        var verticalHeight = Y + 2 * Z + 2 * B - 4 * C;
        
        // 确保图层顺序在最上方
        var topIndex = psdImage.Layers.Length;
        var lineGroup = psdImage.AddLayerGroup("GuidelineGroup", topIndex, false);

        // Y轴辅助线 (水平线)
        CreateGuidelineLayer(psdImage, "guideline_Y001", 0, B, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y002", 0, B + Z - 2 * C, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y003", 0, B + Z, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y004", 0, B + Z + Y, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y005", 0, B + Z + Y + 2 * C, horizontalWidth, 1, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_Y006", 0, B + Z + Y + Z, horizontalWidth, 1, lineGroup);

        // X轴辅助线 (垂直线)
        CreateGuidelineLayer(psdImage, "guideline_X001", A, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X002", A + Z, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X003", A + Z + X, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X004", A + 2 * Z + X, 0, 1, verticalHeight, lineGroup);
        CreateGuidelineLayer(psdImage, "guideline_X006", A + 2 * Z + 2 * X, 0, 1, verticalHeight, lineGroup);
    }

    private void CreateGuidelineLayer(PsdImage psdImage, string layerName, int x, int y, int width, int height, LayerGroup lineGroup)
    {
        // 使用洋红色标记辅助线，显眼
        var magentaColor = Color.FromArgb(255, 0, 108);
        CreateWhiteRectangleLineLayer(psdImage, layerName, new Rectangle(x, y, width, height), magentaColor, lineGroup);
    }

    /// <summary>
    /// 创建辅助线专用的矩形图层（支持添加到组）
    /// </summary>
    private void CreateWhiteRectangleLineLayer(PsdImage psdImage, string layerName, Rectangle rect, Color layerColor, LayerGroup lineGroup)
    {
        if (psdImage == null) return;

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            Console.WriteLine($"CreateWhiteRectangleLineLayer: invalid size W={rect.Width}, H={rect.Height}");
            return;
        }

        try
        {
            var layer = FillLayer.CreateInstance(FillType.Color);
            if (layer == null) return;

            layer.DisplayName = layerName ?? "guideline";
            
            // 关键：将图层添加到组中，而不是直接添加到 psdImage
            lineGroup.AddLayer(layer);

            var vectorPath = VectorDataProvider.CreateVectorPathForLayer(layer);
            if (vectorPath == null) return;

            vectorPath.FillColor = layerColor;

            var shape = new PathShapeGen();
            // 构建矩形路径
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

    /// <summary>
    /// 创建普通形状图层 (保持原有逻辑不变)
    /// </summary>
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
    /// 核心工具：厘米转像素 (基于 300 DPI)
    /// </summary>
    private int CmToPixels(double cm)
    {
        return (int)Math.Round((cm / 2.54) * DPI);
    }
    
}
