using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TaoAdmin.GatewayService;
using Xunit;

namespace TaoAdmin.Tests.Gateway;

/// <summary>
/// 网关资源聚合（stub HttpMessageHandler）：
/// 全部成功返回快照、单服务 HTTP 失败与异常返回 Available=false 占位、
/// 服务清单过滤空配置。
/// </summary>
public class ResourceAggregationServiceTests
{
    /// <summary>脚本化响应桩：按 URL 前缀返回预设响应或抛错。</summary>
    private sealed class ScriptedHandler(Dictionary<string, Func<HttpResponseMessage>> script) : HttpMessageHandler
    {
        public int CallCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var url = request.RequestUri!.ToString();
            foreach (var (prefix, factory) in script)
                if (url.Contains(prefix, StringComparison.Ordinal))
                    return Task.FromResult(factory());
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    private static ResourceAggregationService Create(
        Dictionary<string, string?>? services,
        Dictionary<string, Func<HttpResponseMessage>> script,
        out ScriptedHandler? handlerRef)
    {
        var handler = new ScriptedHandler(script);
        handlerRef = handler;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(services ?? new Dictionary<string, string?>
            {
                ["Monitor:Services:gateway"] = "http://localhost:5000",
                ["Monitor:Services:identity"] = "http://localhost:5001"
            })
            .Build();
        return new ResourceAggregationService(new StubFactory(handler), config, NullLogger<ResourceAggregationService>.Instance);
    }

    private static HttpResponseMessage JsonOk(string body) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task AggregateAsync_全部成功_返回各服务快照()
    {
        var svc = Create(null, new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["5000"] = () => JsonOk("""{"serviceName":"x","cpuPercent":12.5,"memoryPercent":30}"""),
            ["5001"] = () => JsonOk("""{"serviceName":"y","cpuPercent":3,"memoryPercent":40}""")
        }, out _);
        var result = await svc.AggregateAsync();
        Assert.Equal(2, result.Count);
        Assert.All(result, s => Assert.True(s.Available));
        Assert.Contains(result, s => s.ServiceName == "gateway" && s.CpuPercent == 12.5);
        Assert.Contains(result, s => s.ServiceName == "identity" && s.CpuPercent == 3);
    }

    [Fact]
    public async Task AggregateAsync_单服务HTTP失败_占位AvailableFalse不拖垮整份()
    {
        var svc = Create(null, new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["5000"] = () => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable),
            ["5001"] = () => JsonOk("""{"cpuPercent":3}""")
        }, out _);
        var result = await svc.AggregateAsync();
        Assert.Equal(2, result.Count);
        var failed = result.Single(s => s.ServiceName == "gateway");
        Assert.False(failed.Available);
        Assert.True(result.Single(s => s.ServiceName == "identity").Available);
    }

    [Fact]
    public async Task AggregateAsync_单服务请求异常_占位AvailableFalse()
    {
        var svc = Create(null, new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["5000"] = () => throw new HttpRequestException("连接失败"),
            ["5001"] = () => JsonOk("""{"cpuPercent":3}""")
        }, out _);
        var result = await svc.AggregateAsync();
        Assert.Equal(2, result.Count);
        Assert.False(result.Single(s => s.ServiceName == "gateway").Available);
    }

    [Fact]
    public async Task AggregateAsync_服务清单为空_返回空列表()
    {
        var svc = Create(new Dictionary<string, string?>(), new Dictionary<string, Func<HttpResponseMessage>>(), out _);
        var result = await svc.AggregateAsync();
        Assert.Empty(result);
    }

    [Fact]
    public void ConfiguredServices_空地址配置被过滤()
    {
        var svc = Create(new Dictionary<string, string?>
            {
                ["Monitor:Services:gateway"] = "http://localhost:5000",
                ["Monitor:Services:blank"] = ""
            }, new Dictionary<string, Func<HttpResponseMessage>>(), out _);
        var services = svc.ConfiguredServices();
        Assert.Single(services);
        Assert.Equal("gateway", services[0].Name);
    }
}
