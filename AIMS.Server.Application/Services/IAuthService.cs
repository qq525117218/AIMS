using AIMS.Server.Application.DTOs;

namespace AIMS.Server.Application.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, string clientIp);
    Task LogoutAsync(string token);
}