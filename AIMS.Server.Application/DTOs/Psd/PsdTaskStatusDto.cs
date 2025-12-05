namespace AIMS.Server.Application.DTOs.Psd;

public class PsdTaskStatusDto
{
    public string TaskId { get; set; } = string.Empty;
    public int Progress { get; set; } // 0 - 100
    public string Status { get; set; } = "Pending"; // Pending, Processing, Completed, Failed
    public string Message { get; set; } = string.Empty;
    public string? DownloadUrl { get; set; } // 完成后才有值
}