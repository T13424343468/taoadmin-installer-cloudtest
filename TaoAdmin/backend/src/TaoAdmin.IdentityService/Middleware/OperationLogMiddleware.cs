using TaoAdmin.Common.Entities;
using TaoAdmin.Common.MultiTenancy;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Common.Web;
using TaoAdmin.Domain.Entities;
using ISqlSugarClient = SqlSugar.ISqlSugarClient;

namespace TaoAdmin.IdentityService.Middleware;

/// <summary>
/// 操作日志中间件：记录所有写操作（非 GET）的审计轨迹，登录成功/失败也记录。
/// 不落请求体（避免密码等敏感信息入库），只记录方法、路径、结果、IP、耗时与 TraceId。
/// 异步落库，不阻塞请求；审计失败不影响业务。
/// </summary>
public class OperationLogMiddleware(RequestDelegate next, ILogger<OperationLogMiddleware> logger)
{
    private static readonly HashSet<string> LoginPaths = new(StringComparer.OrdinalIgnoreCase)
    { "/auth/login", "/api/identity/auth/login", "/login" };

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.HasValue ? context.Request.Path.Value : "";
        var isLogin = LoginPaths.Any(p => path.EndsWith(p, StringComparison.OrdinalIgnoreCase));

        // 登录请求：开启缓冲并预读用户名（只提取 userName 字段，密码绝不入库）
        string? loginUserName = null;
        if (isLogin && context.Request.ContentLength > 0)
        {
            try
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                loginUserName = doc.RootElement.TryGetProperty("userName", out var un) ? un.GetString()
                    : doc.RootElement.TryGetProperty("UserName", out var un2) ? un2.GetString() : null;
            }
            catch { /* 解析失败不影响业务 */ }
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception? error = null;
        try { await next(context); }
        catch (Exception ex) { error = ex; throw; }
        finally
        {
            sw.Stop();
            RecordAsync(context, sw.ElapsedMilliseconds, error, loginUserName);
        }
    }

    private void RecordAsync(HttpContext context, long elapsedMs, Exception? error, string? loginUserName)
    {
        try
        {
            var path = context.Request.Path.HasValue ? context.Request.Path.Value : "";
            var isLogin = LoginPaths.Any(p => path.EndsWith(p, StringComparison.OrdinalIgnoreCase));
            var isWrite = !HttpMethods.IsGet(context.Request.Method);
            if (!isLogin && !isWrite) return;
            // 未登录访问受保护接口（401）不记录，避免噪声
            if (!isLogin && context.Response.StatusCode == 401) return;

            var user = context.User;
            var isAuthed = user.Identity?.IsAuthenticated == true;
            var query = context.Request.QueryString.Value ?? "";
            var log = new SysOperationLog
            {
                UserId = isAuthed ? user.GetUserId() : 0,
                // 登录请求记录尝试的账号（含失败尝试，便于审计暴力破解）；业务请求记当前登录人
                UserName = isLogin ? (loginUserName ?? "") : isAuthed ? user.GetUserName() : "",
                TenantId = isAuthed ? user.GetTenantId() : 0,
                Category = isLogin ? "login" : "operation",
                Method = context.Request.Method,
                Path = path.Length > 200 ? path[..200] : path,
                QueryString = query.Length > 500 ? query[..500] : query.Length > 0 ? query : null,
                StatusCode = context.Response.StatusCode,
                Success = context.Response.StatusCode < 400 && error is null
                          && !Equals(context.Items["BizFail"], true) ? 1 : 0,
                Ip = context.Connection.RemoteIpAddress?.ToString() ?? "",
                ElapsedMs = (int)elapsedMs,
                TraceId = context.TraceIdentifier,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            _ = Task.Run(async () =>
            {
                using var scope = context.RequestServices.CreateScope();
                log.Id = scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>().NextId();
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                await db.Insertable(log).ExecuteCommandAsync();
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "操作日志记录失败");
        }
    }
}
