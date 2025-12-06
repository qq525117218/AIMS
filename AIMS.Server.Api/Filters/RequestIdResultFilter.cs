using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIMS.Server.Api.Filters;

public class RequestIdResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // 拦截 ObjectResult (即控制器返回的 Ok(...), BadRequest(...) 等包含数据的对象)
        if (context.Result is ObjectResult objectResult && objectResult.Value != null)
        {
            var value = objectResult.Value;
            var type = value.GetType();

            // 使用反射查找名为 "RequestId" 的属性
            // 这样做的好处是：它不仅支持 ApiResponse<T>，也支持其他任何包含 RequestId 的 DTO
            var requestIdProp = type.GetProperty("RequestId");

            if (requestIdProp != null && requestIdProp.CanWrite)
            {
                // 如果当前 RequestId 为空，则填充 TraceIdentifier
                var currentVal = requestIdProp.GetValue(value) as string;
                if (string.IsNullOrEmpty(currentVal))
                {
                    requestIdProp.SetValue(value, context.HttpContext.TraceIdentifier);
                }
            }
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // 不需要处理
    }
}