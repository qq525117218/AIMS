namespace AIMS.Server.Domain.Interfaces;

public interface IJwtProvider
{
    string GenerateToken(string username, int expireMinutes);
}