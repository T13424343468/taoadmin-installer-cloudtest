using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace TaoAdmin.Common.MultiTenancy;

/// <summary>
/// 租户上下文：请求期间当前租户的载体。
/// 由 TenantMiddleware 解析（优先 X-Tenant-Id 请求头，其次 JWT tenant_id 声明）。
/// 约定：TenantId=0 表示平台层（超管/公共数据），查询时不过滤租户。
/// </summary>
public static class TenantContext
{
    private static readonly AsyncLocal<long?> _current = new();

    /// <summary>当前租户 ID（0 = 平台层）</summary>
    public static long CurrentId => _current.Value ?? 0;

    /// <summary>是否启用租户过滤（多租户总开关，配置 MultiTenant:Enabled）</summary>
    public static bool FilterEnabled { get; set; } = true;

    /// <summary>设置当前请求的租户（仅中间件调用）</summary>
    public static void Set(long tenantId) => _current.Value = tenantId;

    /// <summary>解析请求中的租户：请求头 X-Tenant-Id 优先，其次 JWT 声明 tenant_id</summary>
    public static long Resolve(HttpContext context)
    {
        var header = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (long.TryParse(header, out var fromHeader) && fromHeader >= 0)
            return fromHeader;

        var claim = context.User.FindFirst("tenant_id")?.Value;
        return long.TryParse(claim, out var fromClaim) ? fromClaim : 0;
    }
}

/// <summary>租户中间件：放在 UseAuthentication 之后，为每个请求注入租户上下文</summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        TenantContext.FilterEnabled = configuration.GetValue<bool>("MultiTenant:Enabled", true);
        TenantContext.Set(TenantContext.Resolve(context));
        await _next(context);
    }
}

/// <summary>ClaimsPrincipal 扩展：从令牌读租户 ID</summary>
public static class TenantPrincipalExtensions
{
    public static long GetTenantId(this ClaimsPrincipal user)
        => long.TryParse(user.FindFirst("tenant_id")?.Value, out var id) ? id : 0;
}
