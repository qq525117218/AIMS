namespace AIMS.Server.Application.DTOs.Psd;

public class PrintConfigDto
{
    public double BleedX { get; set; } // 对应 bleed_x
    public double BleedY { get; set; } // 对应 bleed_y
    public double BleedInner { get; set; } // 对应 bleed_inner
    public int ResolutionDpi { get; set; } = 300;
}