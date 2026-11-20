namespace TaoAdmin.Common.Http;

/// <summary>
/// 统一返回格式：Code（业务码）、Message、Data、TraceId。
/// Code = 200 表示成功；401 未认证；403 无权限；其余为业务/系统错误。
/// </summary>
public class ApiResult<T>
{
    public int Code { get; set; }
    /// <summary>是否成功（Code == 200），与前端契约对齐</summary>
    public bool Success { get; set; } = true;
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public string? TraceId { get; set; }

    public static ApiResult<T> Ok(T? data, string message = "success", string? traceId = null)
        => new() { Code = 200, Success = true, Message = message, Data = data, TraceId = traceId };

    public static ApiResult<T> Fail(string message, int code = 500, string? traceId = null)
        => new() { Code = code, Success = false, Message = message, Data = default, TraceId = traceId };
}

/// <summary>无数据的统一返回（可视为 ApiResult&lt;object&gt; 的工厂）</summary>
public static class ApiResult
{
    public static ApiResult<object> Ok(string message = "success", string? traceId = null)
        => new() { Code = 200, Success = true, Message = message, Data = null, TraceId = traceId };

    public static ApiResult<object> Fail(string message, int code = 500, string? traceId = null)
        => new() { Code = code, Success = false, Message = message, Data = null, TraceId = traceId };
}
