using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AIMS.Server.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AIMS.Server.Infrastructure.Auth;

public class JwtProvider : IJwtProvider
{
    private readonly IConfiguration _config;

    public JwtProvider(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(string username, int expireMinutes)
    {
        var secret = _config["JWT_SECRET"] ?? _config["Jwt:SecretKey"];
        var issuer = _config["Jwt:Issuer"] ?? "AIMS_Server";
        var audience = _config["Jwt:Audience"] ?? "AIMS_Client";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expireMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
