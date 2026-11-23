using System.Net;
using System.Text.Json;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 钉钉 Webhook 发送测试：假 Handler 模拟接口应答，
/// 覆盖成功/errcode 非零/HTTP 错误/Webhook 未配置/网络异常。
/// </summary>
public class DingTalkSenderTests
{
    private class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<string> RequestUrls { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUrls.Add(request.RequestUri!.ToString());
            RequestBodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return _responder(request);
        }
    }

    private static SqlSugarClient CreateDb(string? webhook)
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        if (webhook != null)
            db.Insertable(new SysSiteConfig { ConfigKey = "notify_dingtalk_webhook", ConfigValue = webhook })
                .ExecuteCommand();
        return db;
    }

    [Fact]
    public async Task SendAsync_ErrcodeZero_ShouldReturnSuccess()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"errcode\":0,\"errmsg\":\"ok\"}")
        });
        var sender = new DingTalkSender(CreateDb("https://oapi.dingtalk.com/robot/send?access_token=x"),
            new HttpClient(handler));

        var (ok, msg) = await sender.SendAsync("标题", "内容");

        Assert.True(ok, msg);
        Assert.Equal("发送成功", msg);
        Assert.Equal("https://oapi.dingtalk.com/robot/send?access_token=x", handler.RequestUrls[0]);
        var payload = JsonDocument.Parse(handler.RequestBodies[0]);
        Assert.Equal("text", payload.RootElement.GetProperty("msgtype").GetString());
        Assert.Contains("标题", payload.RootElement.GetProperty("text").GetProperty("content").GetString());
        Assert.Contains("内容", payload.RootElement.GetProperty("text").GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendAsync_ErrcodeNonZero_ShouldReturnFailureWithBody()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"errcode\":310000,\"errmsg\":\"sign not match\"}")
        });
        var sender = new DingTalkSender(CreateDb("https://oapi.dingtalk.com/robot/send?access_token=x"),
            new HttpClient(handler));

        var (ok, msg) = await sender.SendAsync("标题", "内容");

        Assert.False(ok);
        Assert.Contains("sign not match", msg);
    }

    [Fact]
    public async Task SendAsync_HttpError_ShouldReturnFailureWithStatus()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var sender = new DingTalkSender(CreateDb("https://oapi.dingtalk.com/robot/send?access_token=x"),
            new HttpClient(handler));

        var (ok, msg) = await sender.SendAsync("标题", "内容");

        Assert.False(ok);
        Assert.Contains("403", msg);
    }

    [Fact]
    public async Task SendAsync_WebhookMissing_ShouldFailWithoutRequest()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("不应发起请求"));
        var sender = new DingTalkSender(CreateDb(null), new HttpClient(handler));

        var (ok, msg) = await sender.SendAsync("标题", "内容");

        Assert.False(ok);
        Assert.Equal("钉钉 Webhook 未配置", msg);
        Assert.Empty(handler.RequestUrls);
    }

    [Fact]
    public async Task SendAsync_NetworkThrows_ShouldReturnFailure()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("连接被拒绝"));
        var sender = new DingTalkSender(CreateDb("https://oapi.dingtalk.com/robot/send?access_token=x"),
            new HttpClient(handler));

        var (ok, msg) = await sender.SendAsync("标题", "内容");

        Assert.False(ok);
        Assert.Contains("钉钉发送失败", msg);
        Assert.Contains("连接被拒绝", msg);
    }
}
