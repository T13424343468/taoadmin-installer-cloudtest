using System.Collections.Concurrent;

namespace TaoAdmin.Common.Resilience;

/// <summary>
/// 熔断器中间件（【网关】侧）：保护下游微服务。
/// 原理（经典三态）：
///   关闭 —— 正常放行，连续失败次数 +1；
///   打开 —— 连续失败达到阈值（默认 5 次）后熔断，直接返回 503，不再请求下游；
///   半开 —— 熔断冷却期（默认 30 秒）后放一个探测请求，成功则恢复，失败则重新熔断。
/// 统计粒度：路由前缀（/api/identity、/api/job、/api/monitor）。
/// </summary>
public class CircuitBreakerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<CircuitBreakerMiddleware> _logger;

    private class CircuitState
    {
        public int ConsecutiveFailures;
        public DateTime OpenedAt = DateTime.MinValue;
        public bool IsOpen;
    }

    private readonly ConcurrentDictionary<string, CircuitState> _circuits = new();

    public CircuitBreakerMiddleware(RequestDelegate next, IConfiguration configuration,
        ILogger<CircuitBreakerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _options = configuration.GetSection("CircuitBreaker").Get<CircuitBreakerOptions>() ?? new();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // 只对转发到下游的业务路径做熔断，健康检查与错误页跳过
        var route = context.Request.Path.Value ?? "";
        if (!route.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        var circuitName = GetCircuitName(route);
        var state = _circuits.GetOrAdd(circuitName, _ => new CircuitState());

        // 半开探测：熔断冷却期满，放一个请求出去试
        if (state.IsOpen)
        {
            if (DateTime.Now - state.OpenedAt < TimeSpan.FromSeconds(_options.BreakSeconds))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = 503, success = false,
                    message = $"服务[{circuitName}]熔断中，请稍后重试（连续失败 {_options.FailureThreshold} 次触发保护）"
                });
                _logger.LogWarning("熔断拦截：{Circuit} 熔断期内拒绝请求", circuitName);
                return;
            }
            state.IsOpen = false;
            _logger.LogInformation("熔断半开：{Circuit} 冷却期满，放行探测请求", circuitName);
        }

        try
        {
            await _next(context);

            if (context.Response.StatusCode >= 500)
            {
                // 下游 5xx 计为失败
                RecordFailure(state, circuitName);
            }
            else
            {
                RecordSuccess(state, circuitName);
            }
        }
        catch (Exception)
        {
            // 连接失败（下游挂了/超时）同样计为失败，再抛给异常处理
            RecordFailure(state, circuitName);
            throw;
        }
    }

    private void RecordFailure(CircuitState state, string circuitName)
    {
        state.ConsecutiveFailures++;
        if (state.ConsecutiveFailures >= _options.FailureThreshold && !state.IsOpen)
        {
            state.IsOpen = true;
            state.OpenedAt = DateTime.Now;
            _logger.LogWarning("熔断打开：{Circuit} 连续失败 {Count} 次，冷却 {Seconds} 秒",
                circuitName, state.ConsecutiveFailures, _options.BreakSeconds);
        }
    }

    private void RecordSuccess(CircuitState state, string circuitName)
    {
        if (state.ConsecutiveFailures > 0)
            _logger.LogInformation("熔断恢复：{Circuit} 探测成功，恢复正常转发", circuitName);
        state.ConsecutiveFailures = 0;
        state.IsOpen = false;
    }

    /// <summary>取路由前缀作为熔断单元：/api/identity/xxx → identity</summary>
    private static string GetCircuitName(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : path;
    }
}

/// <summary>熔断配置（appsettings.json CircuitBreaker 节）</summary>
public class CircuitBreakerOptions
{
    /// <summary>是否启用熔断</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>连续失败多少次后熔断</summary>
    public int FailureThreshold { get; set; } = 5;
    /// <summary>熔断冷却时长（秒），期满后半开探测</summary>
    public int BreakSeconds { get; set; } = 30;
}
