using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Idempotency;
using Xunit;

namespace TaoAdmin.Tests.Idempotency;

/// <summary>幂等存储：写入 / 读取 / TTL 过期</summary>
public class IdempotencyStoreTests
{
    [Fact]
    public void Save_Then_TryGet_ShouldReturnSameBody()
    {
        var store = new IdempotencyStore();
        store.Save("k1", "{\"code\":200}", TimeSpan.FromSeconds(60));
        Assert.Equal("{\"code\":200}", store.TryGet("k1"));
    }

    [Fact]
    public void TryGet_UnknownKey_ShouldReturnNull()
    {
        var store = new IdempotencyStore();
        Assert.Null(store.TryGet("nope"));
    }

    [Fact]
    public void TryGet_AfterTtlExpiry_ShouldReturnNull()
    {
        var store = new IdempotencyStore();
        store.Save("k", "v", TimeSpan.FromMilliseconds(1));
        System.Threading.Thread.Sleep(30);
        Assert.Null(store.TryGet("k"));
    }

    [Fact]
    public void Save_SameKeyTwice_ShouldOverwrite()
    {
        var store = new IdempotencyStore();
        store.Save("k", "old", TimeSpan.FromSeconds(60));
        store.Save("k", "new", TimeSpan.FromSeconds(60));
        Assert.Equal("new", store.TryGet("k"));
    }
}

/// <summary>幂等过滤器：防重放端到端（内存实现，不依赖 Redis）</summary>
public class IdempotencyFilterTests
{
    private static ActionExecutingContext BuildContext(bool requireMarker = true, string? idempotencyKey = "key-A")
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("tenant_id", "1"), new Claim("UserId", "9") }, "Bearer"));
        http.Request.Path = "/api/order";
        if (idempotencyKey != null) http.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var descriptor = new ActionDescriptor();
        if (requireMarker) descriptor.EndpointMetadata = new List<object> { new IdempotentAttribute() };

        var actionContext = new ActionContext(http, new Microsoft.AspNetCore.Routing.RouteData(), descriptor);
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(),
            new Dictionary<string, object?>(), new object());
    }

    [Fact]
    public async Task MissingIdempotencyKey_ShouldRejectWith400_AndNotExecuteBusiness()
    {
        var ctx = BuildContext(idempotencyKey: null);
        var businessExecuted = false;
        var filter = new IdempotencyFilter(new IdempotencyStore());

        await filter.OnActionExecutionAsync(ctx, new ActionExecutionDelegate(() =>
        {
            businessExecuted = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), null!));
        }));

        var json = Assert.IsType<JsonResult>(ctx.Result);
        var body = Assert.IsType<ApiResult<object>>(json.Value);
        Assert.Equal(400, body.Code);
        Assert.False(businessExecuted); // 业务方法未执行
    }

    [Fact]
    public async Task FirstRequest_ShouldExecute_AndCachePayload()
    {
        var ctx = BuildContext();
        var store = new IdempotencyStore();
        var filter = new IdempotencyFilter(store);
        var executed = false;

        await filter.OnActionExecutionAsync(ctx, new ActionExecutionDelegate(() =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), null!)
            {
                Result = new ObjectResult(ApiResult<object>.Ok(new { orderId = "A1" }))
            });
        }));

        Assert.True(executed);
        Assert.Null(ctx.Result); // 正常放行，无短路结果
        Assert.False(ctx.HttpContext.Response.Headers.ContainsKey("X-Idempotent-Replayed"));
        var cacheKey = ctx.HttpContext.Request.Headers["Idempotency-Key"].ToString();
        Assert.False(string.IsNullOrEmpty(store.TryGet($"1:9:/api/order:{cacheKey}")));
    }

    [Fact]
    public async Task SecondRequest_WithSameKey_ShouldReplayFirstResult_WithoutExecutingBusiness()
    {
        var store = new IdempotencyStore();
        var filter = new IdempotencyFilter(store);

        // 第一次：正常执行
        var ctx1 = BuildContext(idempotencyKey: "key-A");
        var executed1 = false;
        await filter.OnActionExecutionAsync(ctx1, new ActionExecutionDelegate(() =>
        {
            executed1 = true;
            return Task.FromResult(new ActionExecutedContext(ctx1, new List<IFilterMetadata>(), null!)
            {
                Result = new ObjectResult(ApiResult<object>.Ok(new { orderId = "A1" }))
            });
        }));
        Assert.True(executed1);

        // 第二次：同 Key 重放，业务不执行
        var ctx2 = BuildContext(idempotencyKey: "key-A");
        var executed2 = false;
        await filter.OnActionExecutionAsync(ctx2, new ActionExecutionDelegate(() =>
        {
            executed2 = true;
            return Task.FromResult(new ActionExecutedContext(ctx2, new List<IFilterMetadata>(), null!));
        }));

        Assert.False(executed2);
        Assert.Equal("true", ctx2.HttpContext.Response.Headers["X-Idempotent-Replayed"].ToString());
        var json = Assert.IsType<JsonResult>(ctx2.Result);
        var replayed = Assert.IsType<ApiResult<object>>(json.Value);
        Assert.True(replayed.Success);
        Assert.Equal(200, replayed.Code);
    }

    [Fact]
    public async Task DifferentKey_ShouldExecuteAgain()
    {
        var store = new IdempotencyStore();
        var filter = new IdempotencyFilter(store);

        var ctx1 = BuildContext(idempotencyKey: "key-A");
        await filter.OnActionExecutionAsync(ctx1, new ActionExecutionDelegate(() =>
            Task.FromResult(new ActionExecutedContext(ctx1, new List<IFilterMetadata>(), null!)
            {
                Result = new ObjectResult(new { n = 1 })
            })));

        var ctx2 = BuildContext(idempotencyKey: "key-B");
        var executed2 = false;
        await filter.OnActionExecutionAsync(ctx2, new ActionExecutionDelegate(() =>
        {
            executed2 = true;
            return Task.FromResult(new ActionExecutedContext(ctx2, new List<IFilterMetadata>(), null!)
            {
                Result = new ObjectResult(new { n = 2 })
            });
        }));
        Assert.True(executed2);
    }

    [Fact]
    public async Task WithoutIdempotentMarker_ShouldPassThrough()
    {
        var ctx = BuildContext(requireMarker: false);
        var filter = new IdempotencyFilter(new IdempotencyStore());
        var executed = false;
        await filter.OnActionExecutionAsync(ctx, new ActionExecutionDelegate(() =>
        {
            executed = true;
            return Task.FromResult(new ActionExecutedContext(ctx, new List<IFilterMetadata>(), null!));
        }));
        Assert.True(executed);
        Assert.Null(ctx.Result);
    }
}
