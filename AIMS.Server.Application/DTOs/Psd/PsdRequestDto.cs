using System.ComponentModel.DataAnnotations;

namespace AIMS.Server.Application.DTOs.Psd;


public class PsdRequestDto
{
    [Range(0.1, 1000, ErrorMessage = "长度必须大于0")]
    public double Length { get; set; }

    [Range(0.1, 1000, ErrorMessage = "高度必须大于0")]
    public double Height { get; set; }

    [Range(0.1, 1000, ErrorMessage = "宽度必须大于0")]
    public double Width { get; set; }

    public double BleedLeftRight { get; set; }
    public double BleedTopBottom { get; set; }
    public double InnerBleed { get; set; }
}
