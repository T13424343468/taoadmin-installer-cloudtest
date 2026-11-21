using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TaoAdmin.Common.Auth;
using Xunit;

namespace TaoAdmin.Tests.Auth;

/// <summary>
/// 动态权限策略提供器：PERM: 前缀策略动态构造，其余策略回退默认提供器（权限校验缺口补测）。
/// </summary>
public class PermissionPolicyProviderTests
{
    private static PermissionPolicyProvider CreateProvider()
        => new(Options.Create(new AuthorizationOptions()));

    [Fact]
    public async Task GetPolicyAsync_PermPrefix_ShouldBuildPolicyWithRequirement()
    {
        var policy = await CreateProvider().GetPolicyAsync($"{PermissionAttribute.PolicyPrefix}sys:user:add");

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("sys:user:add", requirement.PermsCode);
    }

    [Fact]
    public async Task GetPolicyAsync_PermPrefix_ShouldRequireAuthenticatedUserAndJwtScheme()
    {
        var policy = await CreateProvider().GetPolicyAsync($"{PermissionAttribute.PolicyPrefix}sys:role:del");

        Assert.NotNull(policy);
        Assert.Equal(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, policy!.AuthenticationSchemes.Single());
        Assert.Contains(policy.Requirements, r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task GetPolicyAsync_PermPrefix_ShouldBeCaseInsensitive()
    {
        // 前缀匹配不区分大小写：小写 perm: 同样动态构造
        var policy = await CreateProvider().GetPolicyAsync("perm:sys:user:add");

        Assert.NotNull(policy);
        Assert.NotNull(policy!.Requirements.OfType<PermissionRequirement>().SingleOrDefault());
    }

    [Fact]
    public async Task GetPolicyAsync_NonPermName_ShouldFallbackToDefaultProvider()
    {
        // 未注册同名策略时回退提供器返回 null，而非抛异常
        var policy = await CreateProvider().GetPolicyAsync("SomeOtherPolicy");

        Assert.Null(policy);
    }

    [Fact]
    public async Task DefaultAndFallbackPolicies_ShouldDelegateToFallbackProvider()
    {
        var provider = CreateProvider();

        // 默认策略委托回退提供器：非空且要求已认证
        var defaultPolicy = await provider.GetDefaultPolicyAsync();
        Assert.NotNull(defaultPolicy);
        Assert.Contains(defaultPolicy!.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);

        // 回退策略未配置为 null，配置后委托生效
        Assert.Null(await provider.GetFallbackPolicyAsync());
        var options = Options.Create(new AuthorizationOptions());
        options.Value.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        var provider2 = new PermissionPolicyProvider(options);
        Assert.NotNull(await provider2.GetFallbackPolicyAsync());
    }
}
