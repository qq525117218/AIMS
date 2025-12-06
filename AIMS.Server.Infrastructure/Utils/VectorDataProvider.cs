using Aspose.PSD;
using Aspose.PSD.FileFormats.Core.VectorPaths;
using Aspose.PSD.FileFormats.Psd.Layers;
using Aspose.PSD.FileFormats.Psd.Layers.LayerResources;

namespace AIMS.Server.Infrastructure.Utils;

/// <summary>
/// 辅助类：提供图层和矢量路径对象之间的转换和数据处理功能。
/// </summary>
public static class VectorDataProvider
{
    public static VectorPathGen CreateVectorPathForLayer(Layer psdLayer)
    {
        ValidateLayer(psdLayer);

        Size imageSize = psdLayer.Container.Size;

        // 查找或创建 PSD 中的矢量路径数据资源。
        VectorPathDataResource pathResource = FindVectorPathDataResource(psdLayer, true);
        // 查找或创建纯色填充资源。
        SoCoResource socoResource = FindSoCoResource(psdLayer, true);

        // 使用找到的资源和图像尺寸创建 VectorPath 对象。
        VectorPathGen vectorPath = new VectorPathGen(pathResource, imageSize);
        if (socoResource != null)
        {
            // 如果存在纯色资源，则设置矢量路径的填充颜色。
            vectorPath.FillColor = socoResource.Color;
        }

        return vectorPath;
    }

    /// <summary>
    /// 从 VectorPath 实例更新或替换输入图层的资源。
    /// </summary>
    public static void UpdateLayerFromVectorPath(Layer psdLayer, VectorPathGen vectorPath, bool createIfNotExist = false)
    {
        ValidateLayer(psdLayer);

        // 查找或创建相关的 PSD 资源。
        VectorPathDataResource pathResource = FindVectorPathDataResource(psdLayer, createIfNotExist);
        VogkResource vogkResource = FindVogkResource(psdLayer, createIfNotExist);
        SoCoResource socoResource = FindSoCoResource(psdLayer, createIfNotExist);

        Size imageSize = psdLayer.Container.Size;

        // 将 VectorPath 数据更新到 PSD 资源中。
        UpdateResources(pathResource, vogkResource, socoResource, vectorPath, imageSize);

        // 将更新后的资源替换或添加到图层中。
        ReplaceVectorPathDataResourceInLayer(psdLayer, pathResource, vogkResource, socoResource);
    }

   

    /// <summary>
    /// 将 VectorPath 实例的数据更新到 PSD 资源中。
    /// </summary>
    private static void UpdateResources(VectorPathDataResource pathResource, VogkResource vogkResource,
        SoCoResource socoResource, VectorPathGen vectorPath, Size imageSize)
    {
        // 设置 PSD 资源的版本和属性。
        pathResource.Version = vectorPath.Version;
        pathResource.IsNotLinked = vectorPath.IsNotLinked;
        pathResource.IsDisabled = vectorPath.IsDisabled;
        pathResource.IsInverted = vectorPath.IsInverted;

        List<VectorShapeOriginSettings> originSettings = new List<VectorShapeOriginSettings>();
        List<VectorPathRecord> path = new List<VectorPathRecord>();

        // 添加路径填充规则记录。
        path.Add(new PathFillRuleRecord(null));
        path.Add(new InitialFillRuleRecord(vectorPath.IsFillStartsWithAllPixels));

        // 将每个形状转换为 PSD 路径记录并添加。
        for (ushort i = 0; i < vectorPath.Shapes.Count; i++)
        {
            PathShapeGen shape = vectorPath.Shapes[i];
            shape.ShapeIndex = i;
            path.AddRange(shape.ToVectorPathRecords(imageSize));
            originSettings.Add(new VectorShapeOriginSettings() { IsShapeInvalidated = true, OriginIndex = i });
        }

        pathResource.Paths = path.ToArray();
        vogkResource.ShapeOriginSettings = originSettings.ToArray();

        socoResource.Color = vectorPath.FillColor;
    }

    /// <summary>
    /// 将更新后的资源替换或添加到图层中。
    /// </summary>
    private static void ReplaceVectorPathDataResourceInLayer(Layer psdLayer, VectorPathDataResource pathResource,
        VogkResource vogkResource, SoCoResource socoResource)
    {
        bool pathResourceExist = false;
        bool vogkResourceExist = false;
        bool socoResourceExist = false;

        List<LayerResource> resources = new List<LayerResource>(psdLayer.Resources);

        // 遍历并替换已存在的资源。
        for (int i = 0; i < resources.Count; i++)
        {
            LayerResource resource = resources[i];
            if (resource is VectorPathDataResource)
            {
                resources[i] = pathResource;
                pathResourceExist = true;
            }
            else if (resource is VogkResource)
            {
                resources[i] = vogkResource;
                vogkResourceExist = true;
            }
            else if (resource is SoCoResource)
            {
                resources[i] = socoResource;
                socoResourceExist = true;
            }
        }

        // 如果资源不存在，则添加新资源。
        if (!pathResourceExist)
        {
            resources.Add(pathResource);
        }

        if (!vogkResourceExist)
        {
            resources.Add(vogkResource);
        }

        if (!socoResourceExist)
        {
            resources.Add(socoResource);
        }

        psdLayer.Resources = resources.ToArray();
    }

    /// <summary>
    /// 辅助方法：在图层资源中查找 VectorPathDataResource。
    /// </summary>
    private static VectorPathDataResource FindVectorPathDataResource(Layer psdLayer, bool createIfNotExist = false)
    {
        VectorPathDataResource pathResource = null;
        foreach (var resource in psdLayer.Resources)
        {
            if (resource is VectorPathDataResource)
            {
                pathResource = (VectorPathDataResource)resource;
                break;
            }
        }

        if (createIfNotExist && pathResource == null)
        {
            pathResource = new VmskResource();
        }

        return pathResource;
    }

    /// <summary>
    /// 辅助方法：在图层资源中查找 VogkResource。
    /// </summary>
    private static VogkResource FindVogkResource(Layer psdLayer, bool createIfNotExist = false)
    {
        VogkResource vogkResource = null;
        foreach (var resource in psdLayer.Resources)
        {
            if (resource is VogkResource)
            {
                vogkResource = (VogkResource)resource;
                break;
            }
        }

        if (createIfNotExist && vogkResource == null)
        {
            vogkResource = new VogkResource();
        }

        return vogkResource;
    }

    /// <summary>
    /// 辅助方法：在图层资源中查找 SoCoResource。
    /// </summary>
    private static SoCoResource FindSoCoResource(Layer psdLayer, bool createIfNotExist = false)
    {
        SoCoResource socoResource = null;
        foreach (var resource in psdLayer.Resources)
        {
            if (resource is SoCoResource)
            {
                socoResource = (SoCoResource)resource;
                break;
            }
        }

        if (createIfNotExist && socoResource == null)
        {
            socoResource = new SoCoResource();
        }

        return socoResource;
    }

    /// <summary>
    /// 验证输入图层是否有效。
    /// </summary>
    private static void ValidateLayer(Layer layer)
    {
        if (layer == null)
        {
            throw new ArgumentNullException("The layer is NULL.");
        }

        if (layer.Container == null || layer.Container.Size.IsEmpty)
        {
            throw new ArgumentNullException("The layer should have a Container with no empty size.");
        }
    }
}