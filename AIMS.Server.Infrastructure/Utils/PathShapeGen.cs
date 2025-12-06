using Aspose.PSD;
using Aspose.PSD.FileFormats.Core.VectorPaths;

namespace AIMS.Server.Infrastructure.Utils;

public class PathShapeGen
{
    public PathShapeGen()
    {
        this.Points = new List<BezierKnot>();
        this.PathOperations = PathOperations.CombineShapes;
    }

    public PathShapeGen(LengthRecord lengthRecord, List<BezierKnotRecord> bezierKnotRecords, Size imageSize)
        : this()
    {
        this.IsClosed = lengthRecord.IsClosed;
        this.PathOperations = lengthRecord.PathOperations;
        this.ShapeIndex = lengthRecord.ShapeIndex;
        this.InitFromResources(bezierKnotRecords, imageSize);
    }

    // 属性用于获取或设置形状信息。
    public bool IsClosed { get; set; }
    public PathOperations PathOperations { get; set; }
    public ushort ShapeIndex { get; set; }
    public List<BezierKnot> Points { get; private set; }

    /// <summary>
    /// 将此形状转换为 PSD 路径记录的集合。
    /// </summary>
    public IEnumerable<VectorPathRecord> ToVectorPathRecords(Size imageSize)
    {
        List<VectorPathRecord> shapeRecords = new List<VectorPathRecord>();

        LengthRecord lengthRecord = new LengthRecord();
        lengthRecord.IsClosed = this.IsClosed;
        lengthRecord.BezierKnotRecordsCount = this.Points.Count;
        lengthRecord.PathOperations = this.PathOperations;
        lengthRecord.ShapeIndex = this.ShapeIndex;
        shapeRecords.Add(lengthRecord);

        foreach (var bezierKnot in this.Points)
        {
            shapeRecords.Add(bezierKnot.ToBezierKnotRecord(this.IsClosed, imageSize));
        }

        return shapeRecords;
    }

    /// <summary>
    /// 从 PSD 记录中初始化形状值。
    /// </summary>
    private void InitFromResources(IEnumerable<BezierKnotRecord> bezierKnotRecords, Size imageSize)
    {
        List<BezierKnot> newPoints = new List<BezierKnot>();

        foreach (var record in bezierKnotRecords)
        {
            newPoints.Add(new BezierKnot(record, imageSize));
        }

        this.Points = newPoints;
    }
}