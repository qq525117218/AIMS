namespace AIMS.Server.Application.DTOs.Plm;

public class PlmBaseQueryParam
{
    public string app_key { get; set; }
    public string timestamp { get; set; }
    public string signature { get; set; }
}