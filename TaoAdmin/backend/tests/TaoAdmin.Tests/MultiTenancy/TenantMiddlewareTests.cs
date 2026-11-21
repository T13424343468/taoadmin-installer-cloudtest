using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

using TaoAdmin.Common.MultiTenancy;
using Xunit;

namespace TaoAdmin.Tests.MultiTenancy;

/// <summary>
/// 租户中间件与 ClaimsPrincipal 扩展（租户隔离缺口补测）：
/// 中间件负责总开关 + 注入租户上下文；扩展负责从令牌读租户。
/// </summary>
public class TenantMiddlewareTests : IDisposable
{
    public void Dispose()
    {
        TenantContext.Set(0);
        TenantContext.FilterEnabled = true;
    }

    private static IConfiguration BuildConfig(bool multiTenantEnabled)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MultiTenant:Enabled"] = multiTenantEnabled ? "true" : "false"
            })
            .Build();

    private static async Task InvokeMiddlewareAsync(DefaultHttpContext http, IConfiguration configuration, Func<Task>? next = null)
    {
        var executed = false;
        var middleware = new TenantMiddleware(_ =>
        {
            executed = true;
            return next != null ? next() : Task.CompletedTask;
        });
        await middleware.InvokeAsync(http, configuration);
        Assert.True(executed);
    }

    [Fact]
    public async Task HeaderTenant_ShouldBeInjectedIntoContext()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Tenant-Id"] = "42";
        long? seen = null;

        // AsyncLocal 不向调用方回流：在管道 next 内读取，验证注入生效
        await InvokeMiddlewareAsync(http, BuildConfig(multiTenantEnabled: true),
            () => { seen = TenantContext.CurrentId; return Task.CompletedTask; });

        Assert.Equal(42, seen);
    }

    [Fact]
    public async Task JwtClaim_ShouldBeUsedWhenHeaderAbsent()
    {
        var http = new DefaultHttpContext();
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("tenant_id", "7") }, "Bearer"));
        long? seen = null;

        await InvokeMiddlewareAsync(http, BuildConfig(multiTenantEnabled: true),
            () => { seen = TenantContext.CurrentId; return Task.CompletedTask; });

        Assert.Equal(7, seen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FilterSwitch_ShouldFollowConfiguration(bool enabled)
    {
        var http = new DefaultHttpContext();

        await InvokeMiddlewareAsync(http, BuildConfig(enabled));

        Assert.Equal(enabled, TenantContext.FilterEnabled);
    }

    [Fact]
    public async Task ConcurrentRequests_ShouldKeepOwnTenantEach()
    {
        // 两个并发请求各带各的租户头，互不串号（AsyncLocal 隔离）
        var config = BuildConfig(multiTenantEnabled: true);
        long? seenA = null, seenB = null;

        var taskA = Task.Run(async () =>
        {
            var http = new DefaultHttpContext();
            http.Request.Headers["X-Tenant-Id"] = "101";
            await InvokeMiddlewareAsync(http, config, () =>
            {
                seenA = TenantContext.CurrentId;
                return Task.CompletedTask;
            });
        });
        var taskB = Task.Run(async () =>
        {
            var http = new DefaultHttpContext();
            http.Request.Headers["X-Tenant-Id"] = "202";
            await InvokeMiddlewareAsync(http, config, () =>
            {
                seenB = TenantContext.CurrentId;
                return Task.CompletedTask;
            });
        });

        await Task.WhenAll(taskA, taskB);
        Assert.Equal(101, seenA);
        Assert.Equal(202, seenB);
    }
}

/// <summary>ClaimsPrincipal 扩展：从令牌读租户 ID 的边界口径</summary>
public class TenantPrincipalExtensionTests
{
    [Fact]
    public void GetTenantId_WithClaim_ShouldReturnValue()
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("tenant_id", "9") }, "Bearer"));

        Assert.Equal(9, user.GetTenantId());
    }

    [Fact]
    public void GetTenantId_WithoutClaim_ShouldReturnPlatformZero()
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("UserId", "1") }, "Bearer"));

        Assert.Equal(0, user.GetTenantId());
    }

    [Fact]
    public void GetTenantId_NonNumericClaim_ShouldReturnPlatformZero()
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("tenant_id", "not-a-number") }, "Bearer"));

        Assert.Equal(0, user.GetTenantId());
    }

    [Fact]
    public void GetTenantId_Unauthenticated_ShouldReturnPlatformZero()
    {
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity());

        Assert.Equal(0, user.GetTenantId());
    }
}
