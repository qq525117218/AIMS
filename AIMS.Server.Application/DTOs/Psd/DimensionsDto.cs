using System.ComponentModel.DataAnnotations;

namespace AIMS.Server.Application.DTOs.Psd;

public class DimensionsDto
{
    [Range(0.1, 1000)]
    public double Length { get; set; }
    
    [Range(0.1, 1000)]
    public double Width { get; set; }
    
    [Range(0.1, 1000)]
    public double Height { get; set; }
}