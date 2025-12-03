using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.Options;
using AIMS.Server.Domain.Entities;
using AIMS.Server.Domain.Interfaces;
using Microsoft.Extensions.Options;


namespace AIMS.Server.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepo;
    private readonly IRedisService _redisService;
    private readonly IJwtProvider _jwtProvider;
    private readonly RedisOptions _redisOptions; // 使用强类型配置
    private readonly JwtOptions _jwtOptions;

    public AuthService(
        IUserRepository userRepo, 
        IRedisService redisService, 
        IJwtProvider jwtProvider, 
        IOptions<RedisOptions> redisOptions,
        IOptions<JwtOptions> jwtOptions)
    {
        _userRepo = userRepo;
        _redisService = redisService;
        _jwtProvider = jwtProvider;
        _redisOptions = redisOptions.Value;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, string clientIp)
    {
        // 1. 账号密码校验
        if (!await _userRepo.ValidateUserAsync(request.Username, request.Password))
        {
            throw new UnauthorizedAccessException("用户名或密码错误");
        }

        // 2. 生成 JWT
        var token = _jwtProvider.GenerateToken(request.Username, _jwtOptions.ExpireMinutes);
        
        // 3. 创建 Session (使用充血模型工厂)
        var session = TokenSession.Create(
            request.Username, 
            token, 
            clientIp, 
            _jwtOptions.ExpireMinutes
        );

        // 4. 存入 Redis
        var redisKey = GetRedisKey(token);
        await _redisService.SetAsync(redisKey, session, TimeSpan.FromMinutes(_jwtOptions.ExpireMinutes));

        return new LoginResponse { Token = token, ExpireAt = session.ExpireTime };
    }

    public async Task LogoutAsync(string token)
    {
        var redisKey = GetRedisKey(token);
        await _redisService.RemoveAsync(redisKey);
    }

    private string GetRedisKey(string token) => $"{_redisOptions.Prefix}:login:{token}";
}