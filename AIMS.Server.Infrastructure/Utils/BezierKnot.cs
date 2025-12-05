using Aspose.PSD;
using Aspose.PSD.FileFormats.Core.VectorPaths;

namespace AIMS.Server.Infrastructure.Utils;


public class BezierKnot
{
    // PSD 和图像坐标之间的转换比率。
    private const int ImgToPsdRatio = 256 * 65535;

    public BezierKnot(PointF anchorPoint, PointF controlPoint1, PointF controlPoint2, bool isLinked)
    {
        this.AnchorPoint = anchorPoint;
        this.ControlPoint1 = controlPoint1;
        this.ControlPoint2 = controlPoint2;
        this.IsLinked = isLinked;
    }

    public BezierKnot(BezierKnotRecord bezierKnotRecord, Size imageSize)
    {
        this.IsLinked = bezierKnotRecord.IsLinked;
        this.ControlPoint1 = ResourcePointToPointF(bezierKnotRecord.Points[0], imageSize);
        this.AnchorPoint = ResourcePointToPointF(bezierKnotRecord.Points[1], imageSize);
        this.ControlPoint2 = ResourcePointToPointF(bezierKnotRecord.Points[2], imageSize);
    }

    /// <summary>
    /// 一个简化的构造函数，将锚点作为所有点的坐标。
    /// </summary>
    public BezierKnot(PointF anchorPoint, bool isLinked)
    : this(anchorPoint, anchorPoint, anchorPoint, isLinked)
    {
    }

    // 属性用于获取或设置节点信息。
    public bool IsLinked { get; set; }  
    public PointF ControlPoint1 { get; set; }
    public PointF AnchorPoint { get; set; }
    public PointF ControlPoint2 { get; set; }

    /// <summary>
    /// 将此节点转换为 PSD 格式的 BezierKnotRecord。
    /// </summary>
    public BezierKnotRecord ToBezierKnotRecord(bool isClosed, Size imageSize)
    {
        BezierKnotRecord record = new BezierKnotRecord();
        record.Points = new Point[]
        {
            PointFToResourcePoint(this.ControlPoint1, imageSize),
            PointFToResourcePoint(this.AnchorPoint, imageSize),
            PointFToResourcePoint(this.ControlPoint2, imageSize),
        };
        record.IsLinked = this.IsLinked;
        record.IsClosed = isClosed;

        return record;
    }

    /// <summary>
    /// 按指定的偏移量移动节点的所有点。
    /// </summary>
    public void Shift(float xOffset, float yOffset)
    {
        this.ControlPoint1 = new PointF(this.ControlPoint1.X + xOffset, this.ControlPoint1.Y + yOffset);
        this.AnchorPoint = new PointF(this.AnchorPoint.X + xOffset, this.AnchorPoint.Y + yOffset);
        this.ControlPoint2 = new PointF(this.ControlPoint2.X + xOffset, this.ControlPoint2.Y + yOffset);
    }

    /// <summary>
    /// 将 PSD 资源点坐标转换为常规的浮点坐标。
    /// </summary>
    private static PointF ResourcePointToPointF(Point point, Size imageSize)
    {
        return new PointF(point.Y / (ImgToPsdRatio / imageSize.Width), point.X / (ImgToPsdRatio / imageSize.Height));
    }

    /// <summary>
    /// 将常规的浮点坐标转换为 PSD 资源点坐标。
    /// </summary>
    private static Point PointFToResourcePoint(PointF point, Size imageSize)
    {
        return new Point((int)Math.Round(point.Y * (ImgToPsdRatio / imageSize.Height)), (int)Math.Round(point.X * (ImgToPsdRatio / imageSize.Width)));
    }


}