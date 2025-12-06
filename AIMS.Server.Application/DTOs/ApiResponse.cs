using System.Text.Json.Serialization; // 如果需要自定义字段名，引用这个

namespace AIMS.Server.Application.DTOs;

public class ApiResponse<T>
{
    public int Code { get; set; }
    
    // ✅ 修改：将属性重命名为 IsSuccess，避免与静态方法 Success 重名
    // 在 SnakeCaseLower 策略下，它会自动输出为 "is_success"
    public bool IsSuccess { get; set; } 

    public string Message { get; set; }

    // ✅ 新增：请求追踪 ID
    public string? RequestId { get; set; }

    public T? Data { get; set; }

    /// <summary>
    /// 成功的静态工厂方法
    /// </summary>
    public static ApiResponse<T> Success(T data, string message = "操作成功") 
        => new() { 
            Code = 200, 
            IsSuccess = true, 
            Message = message, 
            Data = data 
        };
        
    /// <summary>
    /// 失败的静态工厂方法
    /// </summary>
    public static ApiResponse<T> Fail(int code, string message) 
        => new() { 
            Code = code, 
            IsSuccess = false, 
            Message = message, 
            Data = default 
        };
}