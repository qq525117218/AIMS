using Aspose.PSD;
using Aspose.PSD.FileFormats.Core.VectorPaths;
using Aspose.PSD.FileFormats.Psd.Layers.LayerResources;

namespace AIMS.Server.Infrastructure.Utils;


public class VectorPathGen
{
     public VectorPathGen(VectorPathDataResource vectorPathDataResource, Size imageSize)
    {
        this.InitFromResource(vectorPathDataResource, imageSize);
    }

    // 属性用于获取或设置矢量路径信息。
    public bool IsFillStartsWithAllPixels { get; set; }
    public List<PathShapeGen> Shapes { get; private set; }
    public Color FillColor { get; set; }
    public int Version { get; set; }
    public bool IsDisabled { get; set; }
    public bool IsNotLinked { get; set; }
    public bool IsInverted { get; set; }

    /// <summary>
    /// 从 PSD 资源中初始化矢量路径值。
    /// </summary>
    private void InitFromResource(VectorPathDataResource resource, Size imageSize)
    {
        List<PathShapeGen> newShapes = new List<PathShapeGen>();
        InitialFillRuleRecord initialFillRuleRecord = null;
        LengthRecord lengthRecord = null;
        List<BezierKnotRecord> bezierKnotRecords = new List<BezierKnotRecord>();

        foreach (var pathRecord in resource.Paths)
        {
            if (pathRecord is LengthRecord)
            {
                if (bezierKnotRecords.Count > 0)
                {
                    newShapes.Add(new PathShapeGen(lengthRecord, bezierKnotRecords, imageSize));
                    lengthRecord = null;
                    bezierKnotRecords.Clear();
                }

                lengthRecord = (LengthRecord)pathRecord;
            }
            else if (pathRecord is BezierKnotRecord)
            {
                bezierKnotRecords.Add((BezierKnotRecord)pathRecord);
            }
            else if (pathRecord is InitialFillRuleRecord)
            {
                initialFillRuleRecord = (InitialFillRuleRecord)pathRecord;
            }
        }

        if (bezierKnotRecords.Count > 0)
        {
            newShapes.Add(new PathShapeGen(lengthRecord, bezierKnotRecords, imageSize));
            lengthRecord = null;
            bezierKnotRecords.Clear();
        }

        this.IsFillStartsWithAllPixels = initialFillRuleRecord != null ? initialFillRuleRecord.IsFillStartsWithAllPixels : false;
        this.Shapes = newShapes;

        this.Version = resource.Version;
        this.IsNotLinked = resource.IsNotLinked;
        this.IsDisabled = resource.IsDisabled;
        this.IsInverted = resource.IsInverted;
    }
}