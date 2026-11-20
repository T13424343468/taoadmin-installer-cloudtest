using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using TaoAdmin.Common.MultiTenancy;
using Xunit;

namespace TaoAdmin.Tests.MultiTenancy;

/// <summary>租户上下文：请求头优先、JWT 声明兜底、AsyncLocal 生命周期</summary>
public class TenantContextTests : IDisposable
{
    public TenantContextTests() => TenantContext.Set(0);

    public void Dispose() => TenantContext.Set(0);

    private static HttpContext HttpContextWith(ClaimsPrincipal user, string? headerTenantId = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = user;
        if (headerTenantId != null) ctx.Request.Headers["X-Tenant-Id"] = headerTenantId;
        return ctx;
    }

    private static ClaimsPrincipal UserWithTenant(long tenantId)
        => new(new ClaimsIdentity(new[] { new Claim("tenant_id", tenantId.ToString()) }, "Bearer"));

    [Fact]
    public void Default_WithoutSet_ShouldBePlatform()
    {
        Assert.Equal(0, TenantContext.CurrentId);
    }

    [Fact]
    public void Set_ShouldScopeToCurrentAsyncFlow()
    {
        TenantContext.Set(88);
        Assert.Equal(88, TenantContext.CurrentId);
        TenantContext.Set(0);
        Assert.Equal(0, TenantContext.CurrentId);
    }

    [Fact]
    public void Resolve_Header_ShouldTakePriorityOverJwtClaim()
    {
        var ctx = HttpContextWith(UserWithTenant(11), headerTenantId: "22");
        Assert.Equal(22, TenantContext.Resolve(ctx));
    }

    [Fact]
    public void Resolve_WithoutHeader_ShouldFallBackToJwtClaim()
    {
        var ctx = HttpContextWith(UserWithTenant(11));
        Assert.Equal(11, TenantContext.Resolve(ctx));
    }

    [Fact]
    public void Resolve_NonNumericHeader_ShouldFallBackToJwtClaim()
    {
        var ctx = HttpContextWith(UserWithTenant(11), headerTenantId: "abc");
        Assert.Equal(11, TenantContext.Resolve(ctx));
    }

    [Fact]
    public void Resolve_NegativeHeader_ShouldFallBackToJwtClaim()
    {
        // 约定：负数请求头非法，回退到令牌声明
        var ctx = HttpContextWith(UserWithTenant(11), headerTenantId: "-1");
        Assert.Equal(11, TenantContext.Resolve(ctx));
    }

    [Fact]
    public void Resolve_NothingAvailable_ShouldReturnZero()
    {
        var ctx = HttpContextWith(new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.Equal(0, TenantContext.Resolve(ctx));
    }

    [Fact]
    public async Task AsyncLocal_ShouldFlowDownButNotUp()
    {
        TenantContext.Set(1);
        await Task.Run(() =>
        {
            // 子异步流继承父流当前值；子流内的修改不会回传父流
            Assert.Equal(1, TenantContext.CurrentId);
            TenantContext.Set(2);
            Assert.Equal(2, TenantContext.CurrentId);
        });
        Assert.Equal(1, TenantContext.CurrentId);
    }
}
