namespace AIMS.Server.Domain.Entities;

public class PackagingDimensions
{
    public double Length { get; private set; }
    public double Height { get; private set; }
    public double Width { get; private set; }
    public double BleedLeftRight { get; private set; }
    public double BleedTopBottom { get; private set; }
    public double InnerBleed { get; private set; }

    public PackagingDimensions(double length, double height, double width, 
        double bleedLR, double bleedTB, double innerBleed)
    {
        if (length <= 0 || height <= 0 || width <= 0)
            throw new ArgumentException("尺寸必须大于0");
            
        Length = length;
        Height = height;
        Width = width;
        BleedLeftRight = bleedLR;
        BleedTopBottom = bleedTB;
        InnerBleed = innerBleed;
    }

    // 计算总画布宽度的业务逻辑 (cm)
    public double GetTotalWidthCm() => Length + (BleedLeftRight * 2);
        
    // 计算总画布高度的业务逻辑 (cm)
    public double GetTotalHeightCm() => Height + (BleedTopBottom * 2);
}