using Microsoft.Extensions.Configuration;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 日志中心聚合查询：多服务分文件（gateway/identity/task.jsonl）聚合展示、
/// 服务过滤、消息渲染、非 JSON 行跳过。临时目录真实文件测试。
/// </summary>
public class MonitorLogAggregationTests : IDisposable
{
    private readonly string _dir;

    public MonitorLogAggregationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "taoadmin-logtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(Path.Combine(_dir, "gateway.jsonl"),
        [
            "{\"@t\":\"2026-11-22T10:00:01.0000000Z\",\"@mt\":\"Gateway {Action} ok\",\"Action\":\"route\",\"Service\":\"taoadmin-gateway\"}",
            "corrupted line"
        ]);
        File.WriteAllLines(Path.Combine(_dir, "identity.jsonl"),
        [
            "{\"@t\":\"2026-11-22T10:00:02.0000000Z\",\"@mt\":\"User {UserId} logged in\",\"UserId\":42,\"Service\":\"taoadmin-identity\"}"
        ]);
        File.WriteAllLines(Path.Combine(_dir, "task.jsonl"),
        [
            "{\"@t\":\"2026-11-22T10:00:03.0000000Z\",\"@m\":\"task rendered msg\",\"Service\":\"taoadmin-task\"}"
        ]);
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private MonitorAppService Create() =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Monitor:LogDir"] = _dir
        }).Build(), new HttpClientFactoryStub());

    private class HttpClientFactoryStub : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public async Task GetLogsAsync_AggregatesAllServiceFiles()
    {
        var (total, items) = await Create().GetLogsAsync(1, 20, null, null, null);
        Assert.Equal(3, total);
        Assert.All(items, i => Assert.NotEmpty(i.message));
        // 三个服务来源都在
        Assert.Contains(items, i => i.service == "taoadmin-gateway");
        Assert.Contains(items, i => i.service == "taoadmin-identity");
        Assert.Contains(items, i => i.service == "taoadmin-task");
    }

    [Fact]
    public async Task GetLogsAsync_RendersTemplateMessage()
    {
        var (_, items) = await Create().GetLogsAsync(1, 20, null, "identity", null);
        var item = Assert.Single(items);
        Assert.Equal("User 42 logged in", item.message);
    }

    [Fact]
    public async Task GetLogsAsync_ServiceFilterNarrowsResults()
    {
        var (total, items) = await Create().GetLogsAsync(1, 20, null, "task", null);
        Assert.Equal(1, total);
        Assert.Equal("task rendered msg", items[0].message);
    }

    [Fact]
    public async Task GetLogsAsync_CorruptedLineSkippedNotCounted()
    {
        var (total, items) = await Create().GetLogsAsync(1, 20, null, "gateway", null);
        Assert.Equal(1, total);
        Assert.Equal("Gateway route ok", items[0].message);
    }
}
