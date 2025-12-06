namespace AIMS.Server.Application.DTOs;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpireAt { get; set; }
}