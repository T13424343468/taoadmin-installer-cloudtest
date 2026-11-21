using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaoAdmin.IdentityService.Middleware;
using Xunit;

namespace TaoAdmin.Tests.Middleware;

/// <summary>
/// 操作日志中间件（日志脱敏/审计缺口补测）：
/// 登录请求体缓冲与预读容错、业务异常照常上抛。落库断言需 ISqlSugarClient 测试替身，见缺口清单。
/// </summary>
public class OperationLogMiddlewareTests
{
    private static IServiceProvider RequestServices()
        => new ServiceCollection().BuildServiceProvider(); // 空容器：后台落库任务失败不影响断言

    private static OperationLogMiddleware CreateMiddleware(Func<HttpContext, Task> next)
        => new(ctx => next(ctx), NullLogger<OperationLogMiddleware>.Instance);

    [Fact]
    public async Task LoginPath_JsonBody_ShouldBufferAndAllowDownstreamReread()
    {
        // 登录路径预读请求体提取 userName（密码不提取不落库），读完复位供下游完整重读
        var body = """{"userName":"alice","password":"Secret#123"}""";
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/auth/login";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        http.Request.ContentLength = body.Length;

        string? seenByDownstream = null;
        await CreateMiddleware(async ctx =>
        {
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            seenByDownstream = await reader.ReadToEndAsync();
            ctx.Response.StatusCode = 200;
        }).InvokeAsync(http);

        Assert.Equal(body, seenByDownstream); // 下游拿到的请求体完整未损
    }

    [Fact]
    public async Task LoginPath_MalformedJson_ShouldNotBreakPipeline()
    {
        // 请求体不是合法 JSON：解析失败静默容忍，业务照常执行
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/identity/auth/login";
        var raw = "not-a-json-body";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        http.Request.ContentLength = raw.Length;

        var reachedBusiness = false;
        await CreateMiddleware(ctx =>
        {
            reachedBusiness = true;
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        Assert.True(reachedBusiness);
        Assert.Equal(200, http.Response.StatusCode);
    }

    [Fact]
    public async Task BusinessThrow_ShouldPropagate_NotSwallowed()
    {
        // 中间件不得吞业务异常：审计失败不影响业务，业务失败也不能被审计掩盖
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/orders";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateMiddleware(_ => throw new InvalidOperationException("业务失败"))
                .InvokeAsync(http));
    }

    [Fact]
    public async Task WriteRequest_ShouldPassThrough()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "PUT";
        http.Request.Path = "/api/users/1";

        var reachedBusiness = false;
        await CreateMiddleware(ctx =>
        {
            reachedBusiness = true;
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        }).InvokeAsync(http);

        Assert.True(reachedBusiness);
        Assert.Equal(204, http.Response.StatusCode);
    }
}
