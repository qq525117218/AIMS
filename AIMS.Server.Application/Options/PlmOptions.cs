namespace AIMS.Server.Application.Options;

public class PlmOptions
{
    public const string SectionName = "Plm";

    public string AppKey { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}