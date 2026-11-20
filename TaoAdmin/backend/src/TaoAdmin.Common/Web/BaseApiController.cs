using System.Security.Claims;
using TaoAdmin.Common.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.Common.Web;

/// <summary>
/// 控制器基类：提供带 TraceId 的统一返回方法 ApiOk / ApiFail。
/// 所有业务控制器继承本类。
/// </summary>
[ApiController]
[Authorize] // 默认要求认证（网关已完成 JWT 校验，服务内二次校验）
public abstract class BaseApiController : ControllerBase
{
    /// <summary>成功返回（带数据）</summary>
    protected ApiResult<T> ApiOk<T>(T? data, string message = "success")
        => ApiResult<T>.Ok(data, message, HttpContext.TraceIdentifier);

    /// <summary>成功返回（不带数据）</summary>
    protected ApiResult<object> ApiOk(string message = "success")
        => ApiResult.Ok(message, HttpContext.TraceIdentifier);

    /// <summary>失败返回</summary>
    protected ApiResult<T> ApiFail<T>(string message, int code = 400)
        => ApiResult<T>.Fail(message, code, HttpContext.TraceIdentifier);

    /// <summary>客户端 IP（反代场景取 X-Forwarded-For 首段）</summary>
    protected string Ip
    {
        get
        {
            var fwd = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            return string.IsNullOrWhiteSpace(fwd) ? HttpContext.Connection.RemoteIpAddress?.ToString() ?? "" : fwd.Split(',')[0].Trim();
        }
    }
}

/// <summary>当前登录用户信息读取扩展</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>获取当前用户ID</summary>
    public static long GetUserId(this ClaimsPrincipal user)
        => long.TryParse(user.FindFirst("UserId")?.Value, out var id) ? id : 0;

    /// <summary>获取当前用户名</summary>
    public static string GetUserName(this ClaimsPrincipal user)
        => user.Identity?.Name ?? string.Empty;

    /// <summary>获取当前用户权限码集合</summary>
    public static IEnumerable<string> GetPermissions(this ClaimsPrincipal user)
        => user.FindAll(Auth.PermissionAuthorizationHandler.PermClaimType).Select(c => c.Value);

    /// <summary>获取当前用户角色编码集合</summary>
    public static List<string> GetRoles(this ClaimsPrincipal user)
        => user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value).ToList();

    /// <summary>是否超级管理员（拥有 admin 角色即视为超管）</summary>
    public static bool IsSuperAdmin(this ClaimsPrincipal user)
        => user.IsInRole(Auth.PermissionAuthorizationHandler.SuperAdminRole);
}
