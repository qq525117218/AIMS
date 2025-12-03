using AIMS.Server.Application.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIMS.Server.Api.Filters;

public class GlobalExceptionFilter : IAsyncExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger, IHostEnvironment env)
    {
        _logger = logger;
        _env = env;
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        // 记录结构化日志
        _logger.LogError(context.Exception, "Unhandled exception occurred: {Message}", context.Exception.Message);

        int statusCode;
        string message;

        // 根据异常类型决定返回码
        switch (context.Exception)
        {
            case UnauthorizedAccessException:
                statusCode = StatusCodes.Status401Unauthorized;
                message = context.Exception.Message;
                break;
            case ArgumentException:
                statusCode = StatusCodes.Status400BadRequest;
                message = context.Exception.Message;
                break;
            default:
                statusCode = StatusCodes.Status500InternalServerError;
                message = _env.IsDevelopment() ? context.Exception.ToString() : "服务器内部错误，请联系管理员";
                break;
        }

        context.Result = new JsonResult(ApiResponse<string>.Fail(statusCode, message))
        {
            StatusCode = statusCode
        };
        
        context.ExceptionHandled = true;
        return Task.CompletedTask;
    }
}