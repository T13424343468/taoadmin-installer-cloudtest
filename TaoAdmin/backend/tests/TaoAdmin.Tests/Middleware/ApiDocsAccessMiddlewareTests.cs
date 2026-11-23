using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using TaoAdmin.Common.Web;
using Xunit;

namespace TaoAdmin.Tests.Middleware;

/// <summary>
/// ⑥ 接口文档访问限制中间件实效断言：
/// LocalOnly 模式下远程非本机返回 404（文档结构不外泄）、本机放行；
/// Authenticated 模式下未登录 401、已登录放行；Open 模式全放行；非文档路径永不拦截。
/// </summary>
public class ApiDocsAccessMiddlewareTests
{
    private static ApiDocsAccessMiddleware Create(string mode, Func<HttpContext, Task> next)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DocsAccess:Mode"] = mode }).Build();
        return new ApiDocsAccessMiddleware(ctx => next(ctx), config);
    }

    private static HttpContext DocsContext(string remoteIp = "10.20.30.40", bool authenticated = false)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/scalar";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        if (authenticated)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "alice") }, "TestAuth"));
        }
        return ctx;
    }

    [Fact]
    public async Task LocalOnly_RemoteIp_ShouldReturn404()
    {
        var reached = false;
        var mw = Create("LocalOnly", _ => { reached = true; return Task.CompletedTask; });
        await mw.InvokeAsync(DocsContext());
        Assert.False(reached); // 实效断言：下游未被触达
        var ctx = DocsContext();
        await mw.InvokeAsync(ctx);
        Assert.Equal(404, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task LocalOnly_RemoteIp_ResponseStatusCode_Is404()
    {
        var ctx = DocsContext();
        var mw = Create("LocalOnly", _ => Task.CompletedTask);
        await mw.InvokeAsync(ctx);
        Assert.Equal(404, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task LocalOnly_Loopback_ShouldPassThrough()
    {
        var reached = false;
        var mw = Create("LocalOnly", _ => { reached = true; return Task.CompletedTask; });
        await mw.InvokeAsync(DocsContext(remoteIp: "127.0.0.1"));
        Assert.True(reached);
    }

    [Fact]
    public async Task Authenticated_Unauthenticated_ShouldReturn401()
    {
        var ctx = DocsContext();
        var mw = Create("Authenticated", _ => Task.CompletedTask);
        await mw.InvokeAsync(ctx);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Authenticated_ShouldPassThrough()
    {
        var reached = false;
        var mw = Create("Authenticated", _ => { reached = true; return Task.CompletedTask; });
        await mw.InvokeAsync(DocsContext(authenticated: true));
        Assert.True(reached);
    }

    [Fact]
    public async Task Open_ShouldPassThrough()
    {
        var reached = false;
        var mw = Create("Open", _ => { reached = true; return Task.CompletedTask; });
        await mw.InvokeAsync(DocsContext());
        Assert.True(reached);
    }

    [Fact]
    public async Task NonDocsPath_ShouldNeverBeBlocked()
    {
        var reached = false;
        var mw = Create("LocalOnly", _ => { reached = true; return Task.CompletedTask; });
        var ctx = DocsContext();
        ctx.Request.Path = "/api/users";
        await mw.InvokeAsync(ctx);
        Assert.True(reached);
    }

    [Fact]
    public async Task DefaultMode_WhenUnset_ShouldBeLocalOnly()
    {
        // 未配置时从严：远程访问文档被拦（能红——若默认放宽此断言即失败）
        var ctx = DocsContext();
        var mw = new ApiDocsAccessMiddleware(_ => Task.CompletedTask,
            new ConfigurationBuilder().Build());
        await mw.InvokeAsync(ctx);
        Assert.Equal(404, ctx.Response.StatusCode);
    }
}
