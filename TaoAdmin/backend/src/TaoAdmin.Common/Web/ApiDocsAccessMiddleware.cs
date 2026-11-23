using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace TaoAdmin.Common.Web;

/// <summary>
/// ⑥ 接口文档访问限制中间件：保护 /scalar、/openapi、/swagger 文档端点。
/// 模式（配置 DocsAccess:Mode，缺省 LocalOnly 从严）：
///   LocalOnly —— 仅本机回环地址（127.0.0.1/::1）可访问；
///   Authenticated —— 登录后可看：携带有效 JWT（由认证中间件解析）即可访问；
///   Open —— 完全放开（不推荐，仅供内网明确需要时配置）。
/// </summary>
public class ApiDocsAccessMiddleware
{
    private static readonly HashSet<string> DocsPrefixes = new(StringComparer.OrdinalIgnoreCase)
    { "/scalar", "/openapi", "/swagger" };

    private readonly RequestDelegate _next;
    private readonly DocsAccessMode _mode;

    public ApiDocsAccessMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _mode = configuration["DocsAccess:Mode"]?.ToLowerInvariant() switch
        {
            "open" => DocsAccessMode.Open,
            "authenticated" => DocsAccessMode.Authenticated,
            _ => DocsAccessMode.LocalOnly
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsDocsPath(context.Request.Path) && _mode != DocsAccessMode.Open)
        {
            var remote = context.Connection.RemoteIpAddress;
            var isLocal = remote is not null && System.Net.IPAddress.IsLoopback(remote);

            if (_mode == DocsAccessMode.LocalOnly && !isLocal)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { code = 404, msg = "Not Found" });
                return;
            }

            if (_mode == DocsAccessMode.Authenticated && isLocal)
            {
                // 本机访问始终放行，便于运维直接查看
            }
            else if (_mode == DocsAccessMode.Authenticated &&
                     context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { code = 401, msg = "请先登录后访问接口文档" });
                return;
            }
        }

        await _next(context);
    }

    private static bool IsDocsPath(PathString path)
    {
        foreach (var prefix in DocsPrefixes)
        {
            if (path.StartsWithSegments(prefix))
            {
                return true;
            }
        }
        return false;
    }

    private enum DocsAccessMode { LocalOnly, Authenticated, Open }
}
