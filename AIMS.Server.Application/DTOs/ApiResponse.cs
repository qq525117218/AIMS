namespace AIMS.Server.Application.DTOs;

public class ApiResponse<T>
{
    public int Code { get; set; }
    public string Message { get; set; }
    public T? Data { get; set; }

    public static ApiResponse<T> Success(T data, string message = "操作成功") 
        => new() { Code = 200, Message = message, Data = data };
        
    public static ApiResponse<T> Fail(int code, string message) 
        => new() { Code = code, Message = message, Data = default };
}