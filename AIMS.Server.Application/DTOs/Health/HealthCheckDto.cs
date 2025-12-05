namespace AIMS.Server.Application.DTOs.Health;

public class HealthCheckDto
{
    public string Status { get; set; } = "Unknown"; // Overall Status
    public string Timestamp { get; set; } = string.Empty;
    public List<ComponentHealth> Components { get; set; } = new();
}