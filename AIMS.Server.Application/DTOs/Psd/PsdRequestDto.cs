using System.ComponentModel.DataAnnotations;

namespace AIMS.Server.Application.DTOs.Psd;


public class PsdRequestDto
{
    [Required]
    public string ProjectName { get; set; } = string.Empty;

    public UserContextDto UserContext { get; set; } = new();

    [Required]
    public PackagingSpecsDto Specifications { get; set; } = new();

    public PackagingAssetsDto Assets { get; set; } = new();
}
