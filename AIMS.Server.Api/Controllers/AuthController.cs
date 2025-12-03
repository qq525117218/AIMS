using AIMS.Server.Application.DTOs;
using AIMS.Server.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace AIMS.Server.Api.Controllers;

/// <summary>
/// 用户认证控制器
/// </summary>
/// <remarks>
/// 提供用户登录获取 Token 以及注销登录的功能。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")] // 声明该控制器产出 JSON
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// 用户登录
    /// </summary>
    /// <remarks>
    /// 验证用户名和密码
    /// </remarks>
    /// <param name="request">登录凭证（用户名/密码）</param>
    /// <returns>包含 Token 和过期时间的响应对象</returns>
    /// <response code="200">登录成功</response>
    /// <response code="400">请求参数无效（如字段为空）</response>
    /// <response code="401">用户名或密码错误</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status401Unauthorized)]
    public async Task<ApiResponse<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        // 获取 IP 仍然可以使用 HttpContext，这是标准做法
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            
        var result = await _authService.LoginAsync(request, ip);
        return ApiResponse<LoginResponse>.Success(result, "登录成功");
    }

    /// <summary>
    /// 用户登出
    /// </summary>
    /// <remarks>
    /// 使当前的 JWT Token 立即失效
    /// <br/>
    /// 需要在请求头中携带有效的 Bearer Token。
    /// </remarks>
    /// <param name="authHeader">Authorization 请求头 (格式: Bearer {token})</param>
    /// <returns>操作结果</returns>
    /// <response code="200">登出成功</response>
    /// <response code="400">Token 格式错误或为空</response>
    /// <response code="401">未授权或 Token 已过期</response>
    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status401Unauthorized)]
    public async Task<ApiResponse<string>> Logout([FromHeader(Name = "Authorization")] string? authHeader)
    {
        // 1. 使用模型绑定直接获取 Header
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return ApiResponse<string>.Fail(400, "Token 无效");
        }

        // 2. 去除 "Bearer " 前缀获取纯 Token
        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) 
            ? authHeader.Substring("Bearer ".Length).Trim() 
            : authHeader.Trim();

        if (string.IsNullOrEmpty(token))
        {
            return ApiResponse<string>.Fail(400, "Token 为空");
        }

        // 3. 执行登出业务
        await _authService.LogoutAsync(token);
            
        return ApiResponse<string>.Success(null, "登出成功");
    }
}
