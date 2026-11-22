using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace TaoAdmin.Common.Auth;

/// <summary>
/// 权限需求：携带接口/按钮要求的权限码，例如 "sys:user:add"。
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    /// <summary>要求的权限码</summary>
    public string PermsCode { get; }

    public PermissionRequirement(string permsCode)
    {
        PermsCode = permsCode;
    }
}

/// <summary>
/// 基于权限码的策略授权处理器。
/// 用户登录时，权限码集合已写入 JWT 的 perm 声明；此处把"当前用户权限码"与
/// 特性 [Permission("sys:user:add")] 声明的权限码做匹配，命中即通过。
/// 拥有超级管理员角色（角色码 admin）的用户直接放行。
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    /// <summary>JWT 中权限码使用的 Claim 类型</summary>
    public const string PermClaimType = "perm";

    /// <summary>超级管理员角色码，绕过权限码校验</summary>
    public const string SuperAdminRole = "admin";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // 未认证直接失败
        if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
            return Task.CompletedTask;

        // 超级管理员放行
        if (context.User.IsInRole(SuperAdminRole))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // 匹配用户权限码集合
        var userPerms = context.User.FindAll(PermClaimType).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (userPerms.Contains(requirement.PermsCode))
        {
            context.Succeed(requirement);
        }

        // 不调用 Succeed 即视为拒绝
        return Task.CompletedTask;
    }

    /// <summary>
    /// 接口级授权判定（供 ApiAuthMiddleware 使用）：按 SysApi 记录（路径+HTTP 方法）
    /// 找到接口的权限码，与用户权限码集合匹配。命中返回 true。
    /// 超管直接放行；接口未登记或权限码为空返回 false（由调用方决定策略）。
    /// </summary>
    public static bool HasApiPermission(System.Security.Claims.ClaimsPrincipal? user, string? apiPermsCode)
    {
        if (string.IsNullOrEmpty(apiPermsCode)) return false;
        if (user?.Identity == null || !user.Identity.IsAuthenticated) return false;
        if (user.IsInRole(SuperAdminRole)) return true;
        return user.FindAll(PermClaimType).Any(c => string.Equals(c.Value, apiPermsCode, StringComparison.OrdinalIgnoreCase));
    }
}
