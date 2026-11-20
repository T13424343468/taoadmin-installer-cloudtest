using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace TaoAdmin.Common.Auth;

/// <summary>
/// 权限特性：在 Controller / Action 上标注 [Permission("sys:user:add")] 即要求当前用户
/// 具备该权限码。内部转换为策略名 "PERM:{权限码}"，由 PermissionPolicyProvider 动态解析。
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class PermissionAttribute : AuthorizeAttribute
{
    /// <summary>自定义策略名前缀</summary>
    public const string PolicyPrefix = "PERM:";

    /// <summary>要求的权限码</summary>
    public string PermsCode { get; }

    public PermissionAttribute(string permsCode)
    {
        PermsCode = permsCode;
        Policy = $"{PolicyPrefix}{permsCode}";
    }
}

/// <summary>
/// 自定义授权策略提供器：遇到 "PERM:" 前缀的策略名时，动态构造包含
/// PermissionRequirement 的策略；其余策略走默认提供器（如默认的 JWT 认证策略）。
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionAttribute.PolicyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var permsCode = policyName[PermissionAttribute.PolicyPrefix.Length..];
            return new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permsCode))
                .Build();
        }

        return await _fallback.GetPolicyAsync(policyName);
    }
}
