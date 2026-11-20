using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaoAdmin.Common.Resilience;
using Xunit;

namespace TaoAdmin.Tests.Gateway;

/// <summary>熔断器（网关纯状态逻辑）：关闭 → 打开 → 半开恢复</summary>
public class CircuitBreakerMiddlewareTests
{
    private static IConfiguration Config(int threshold = 3, int breakSeconds = 30, bool enabled = true)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CircuitBreaker:Enabled"] = enabled.ToString(),
            ["CircuitBreaker:FailureThreshold"] = threshold.ToString(),
            ["CircuitBreaker:BreakSeconds"] = breakSeconds.ToString()
        }).Build();

    /// <summary>可切换健康/故障的下游模拟（同一个中间件实例内切换）</summary>
    private sealed class Downstream
    {
        public bool Healthy;
        public int CallCount;
        public bool Throw;

        public RequestDelegate Delegate => ctx =>
        {
            CallCount++;
            if (Throw) throw new InvalidOperationException("boom");
            ctx.Response.StatusCode = Healthy ? 200 : 502;
            return Task.CompletedTask;
        };
    }

    private static async Task<int> InvokeAsync(CircuitBreakerMiddleware middleware, string path = "/api/identity/users")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.StatusCode = 200;
        await middleware.InvokeAsync(ctx);
        return ctx.Response.StatusCode;
    }

    [Fact]
    public async Task Disabled_ShouldAlwaysPassThrough()
    {
        var downstream = new Downstream { Healthy = false };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(enabled: false), NullLogger<CircuitBreakerMiddleware>.Instance);
        Assert.Equal(502, await InvokeAsync(mw));
        Assert.Equal(502, await InvokeAsync(mw)); // 不熔断，持续打到下游
    }

    [Fact]
    public async Task NonApiPath_ShouldSkipCircuitBreaker()
    {
        var downstream = new Downstream { Healthy = false };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(), NullLogger<CircuitBreakerMiddleware>.Instance);
        Assert.Equal(502, await InvokeAsync(mw, "/health"));
        Assert.Equal(502, await InvokeAsync(mw, "/health")); // 非 /api 路径不参与熔断
    }

    [Fact]
    public async Task FailuresBelowThreshold_ShouldNotOpenCircuit()
    {
        var downstream = new Downstream { Healthy = false };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(threshold: 3), NullLogger<CircuitBreakerMiddleware>.Instance);

        Assert.Equal(502, await InvokeAsync(mw));
        Assert.Equal(502, await InvokeAsync(mw));
        Assert.Equal(2, downstream.CallCount); // 未熔断，仍然转发
    }

    [Fact]
    public async Task ReachingThreshold_ShouldOpenCircuit_AndRejectWith503()
    {
        var downstream = new Downstream { Healthy = false };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(threshold: 3), NullLogger<CircuitBreakerMiddleware>.Instance);

        Assert.Equal(502, await InvokeAsync(mw));
        Assert.Equal(502, await InvokeAsync(mw));
        Assert.Equal(502, await InvokeAsync(mw)); // 第 3 次 → 打开

        downstream.Healthy = true;
        Assert.Equal(503, await InvokeAsync(mw));
        Assert.Equal(3, downstream.CallCount); // 熔断期内不再请求下游
    }

    [Fact]
    public async Task HalfOpen_AfterCooldown_ProbeSuccess_ShouldRecover()
    {
        var downstream = new Downstream { Healthy = false };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(threshold: 1, breakSeconds: 0),
            NullLogger<CircuitBreakerMiddleware>.Instance);

        Assert.Equal(502, await InvokeAsync(mw)); // 熔断打开
        downstream.Healthy = true;
        // 冷却 0 秒：立即半开放行，探测成功 → 恢复
        Assert.Equal(200, await InvokeAsync(mw));
        Assert.Equal(2, downstream.CallCount);

        // 恢复后再次失败 → 重新计次（不会立即熔断，因为计数已清零）
        downstream.Healthy = false;
        Assert.Equal(502, await InvokeAsync(mw));
        Assert.Equal(3, downstream.CallCount);
    }

    [Fact]
    public async Task FailedProbe_ShouldReopen_ButStillProbeAgainWithZeroCooldown()
    {
        var downstream = new Downstream { Healthy = false };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(threshold: 1, breakSeconds: 0),
            NullLogger<CircuitBreakerMiddleware>.Instance);

        Assert.Equal(502, await InvokeAsync(mw));  // 打开（冷却 0 秒，随即进入半开）
        Assert.Equal(502, await InvokeAsync(mw));  // 半开探测失败 → 重新熔断
        downstream.Healthy = true;
        Assert.Equal(200, await InvokeAsync(mw));  // 重新熔断后冷却 0 秒，再次放行探测并恢复
        Assert.Equal(3, downstream.CallCount);
    }

    [Fact]
    public async Task DownstreamException_ShouldCountAsFailure()
    {
        var downstream = new Downstream { Healthy = false, Throw = true };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(threshold: 1),
            NullLogger<CircuitBreakerMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(mw));
        downstream.Throw = false;
        Assert.Equal(503, await InvokeAsync(mw)); // 异常计为失败 → 熔断打开
    }

    [Fact]
    public async Task SuccessResponses_ShouldNeverTriggerBreaker()
    {
        var downstream = new Downstream { Healthy = true };
        var mw = new CircuitBreakerMiddleware(downstream.Delegate, Config(threshold: 1),
            NullLogger<CircuitBreakerMiddleware>.Instance);

        for (var i = 0; i < 10; i++)
            Assert.Equal(200, await InvokeAsync(mw));
        Assert.Equal(10, downstream.CallCount);
    }
}
