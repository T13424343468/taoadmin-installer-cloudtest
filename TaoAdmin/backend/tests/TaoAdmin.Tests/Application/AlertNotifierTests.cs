using System.Net;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 告警通知器测试（SQLite 内存库真库）：
/// 规则命中（事件类型/启用/阈值）、渠道发送成功失败留痕、发送失败不阻塞、测试消息。
/// </summary>
public class AlertNotifierTests
{
    /// <summary>邮件发送测试替身：可配置结果与异常。</summary>
    private class FakeEmailSender : IEmailSender
    {
        public int Calls;
        public (bool Ok, string Message) Result = (true, "发送成功");
        public Exception? Throw;

        public Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body)
        {
            Calls++;
            if (Throw != null) throw Throw;
            return Task.FromResult(Result);
        }
    }

    private static SqlSugarClient CreateDb()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig, SysAlertRule, SysNotifyLog>();
        return db;
    }

    private static AlertNotifier CreateNotifier(SqlSugarClient db, FakeEmailSender? email = null,
        DingTalkSender? dingtalk = null)
        => new(db, new SnowflakeIdGenerator(1, 1), email ?? new FakeEmailSender(),
            dingtalk ?? new DingTalkSender(db, new System.Net.Http.HttpClient(new FakeDingTalkHandler())));

    private class FakeDingTalkHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("{\"errcode\":0,\"errmsg\":\"ok\"}")
            });
    }

    private static readonly TaoAdmin.Common.Snowflake.SnowflakeIdGenerator snowflake = new(9, 9);

    private static async Task SeedRule(SqlSugarClient db, string eventType, int threshold,
        string channels = "email", bool enabled = true)
    {
        await db.Insertable(new SysAlertRule
        {
            Id = snowflake.NextId(),
            Name = $"rule-{eventType}-{threshold}",
            EventType = eventType,
            Threshold = threshold,
            Channels = channels,
            Enabled = enabled
        }).ExecuteCommandAsync();
    }

    private static async Task SeedConfig(SqlSugarClient db, string key, string value)
        => await db.Insertable(new SysSiteConfig
        {
            Id = snowflake.NextId(), ConfigKey = key, ConfigValue = value
        }).ExecuteCommandAsync();

    [Fact]
    public async Task NotifyAsync_NoRules_ShouldSendNothing()
    {
        var db = CreateDb();
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", "内容", 3);

        Assert.Equal(0, sent);
        Assert.Equal(0, email.Calls);
        Assert.Equal(0, db.Queryable<SysNotifyLog>().Count());
    }

    [Fact]
    public async Task NotifyAsync_ThresholdNotReached_ShouldNotTrigger()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 3);
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", "内容", 2);

        Assert.Equal(0, sent);
        Assert.Equal(0, email.Calls);
        Assert.Equal(0, db.Queryable<SysNotifyLog>().Count());
    }

    [Fact]
    public async Task NotifyAsync_ThresholdReached_EmailOk_ShouldRecordSuccessLog()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 2);
        await SeedConfig(db, "notify_alert_receivers", "admin@tao.dev");
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "任务连续失败", "任务 X 连续失败 2 次", 2);

        Assert.Equal(1, sent);
        Assert.Equal(1, email.Calls);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.True(log.Success);
        Assert.Equal("email", log.Channel);
        Assert.Equal("admin@tao.dev", log.Target);
        Assert.Equal("任务连续失败", log.Subject);
        Assert.Null(log.Error);
    }

    [Fact]
    public async Task NotifyAsync_EmailFails_ShouldRecordFailure_NotThrow()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 1);
        await SeedConfig(db, "notify_alert_receivers", "admin@tao.dev");
        var email = new FakeEmailSender { Result = (false, "SMTP 服务器未配置") };
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", "内容", 1);

        Assert.Equal(0, sent);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.False(log.Success);
        Assert.Equal("SMTP 服务器未配置", log.Error);
    }

    [Fact]
    public async Task NotifyAsync_EmailThrows_ShouldRecordError_NotAffectOtherChannels()
    {
        var db = CreateDb();
        await SeedRule(db, "SERVICE_DOWN", threshold: 1, channels: "email,dingtalk");
        await SeedConfig(db, "notify_alert_receivers", "admin@tao.dev");
        await SeedConfig(db, "notify_dingtalk_webhook", "https://oapi.dingtalk.com/robot/send?access_token=x");
        var email = new FakeEmailSender { Throw = new InvalidOperationException("网络不可达") };
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("SERVICE_DOWN", "服务不可用", "网关 DOWN", 1);

        // 邮件异常被吞掉并留痕，钉钉照常发送成功
        Assert.Equal(1, sent);
        var logs = db.Queryable<SysNotifyLog>().ToList();
        Assert.Equal(2, logs.Count);
        var emailLog = logs.Single(l => l.Channel == "email");
        Assert.False(emailLog.Success);
        Assert.Contains("网络不可达", emailLog.Error);
        var dingLog = logs.Single(l => l.Channel == "dingtalk");
        Assert.True(dingLog.Success);
    }

    [Fact]
    public async Task NotifyAsync_ReceiversMissing_ShouldFailWithHint()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 1);
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", "内容", 1);

        Assert.Equal(0, sent);
        Assert.Equal(0, email.Calls);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.Contains("notify_alert_receivers", log.Error);
    }

    [Fact]
    public async Task NotifyAsync_UnknownChannelInRule_ShouldRecordUnknownChannelError()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 1, channels: "sms");
        var notifier = CreateNotifier(db);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", "内容", 1);

        Assert.Equal(0, sent);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.False(log.Success);
        Assert.Contains("未知渠道", log.Error);
    }

    [Fact]
    public async Task NotifyAsync_DisabledRule_ShouldNotTrigger()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 1, enabled: false);
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", "内容", 5);

        Assert.Equal(0, sent);
        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task NotifyAsync_OtherEventType_ShouldNotTrigger()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 1);
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var sent = await notifier.NotifyAsync("SERVICE_DOWN", "标题", "内容", 1);

        Assert.Equal(0, sent);
        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task SendTestAsync_Email_ShouldSendAndRecord()
    {
        var db = CreateDb();
        await SeedConfig(db, "notify_alert_receivers", "admin@tao.dev");
        var email = new FakeEmailSender();
        var notifier = CreateNotifier(db, email);

        var (ok, msg) = await notifier.SendTestAsync("email");

        Assert.True(ok, msg);
        Assert.Equal(1, email.Calls);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.True(log.Success);
        Assert.Contains("测试消息", log.Subject);
    }

    [Fact]
    public async Task SendTestAsync_DingTalk_ShouldSendAndRecord()
    {
        var db = CreateDb();
        await SeedConfig(db, "notify_dingtalk_webhook", "https://oapi.dingtalk.com/robot/send?access_token=x");
        var notifier = CreateNotifier(db);

        var (ok, msg) = await notifier.SendTestAsync("dingtalk");

        Assert.True(ok, msg);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.True(log.Success);
        Assert.Equal("dingtalk", log.Channel);
    }

    [Fact]
    public async Task SendTestAsync_UnknownChannel_ShouldFailWithoutLog()
    {
        var db = CreateDb();
        var notifier = CreateNotifier(db);

        var (ok, msg) = await notifier.SendTestAsync("sms");

        Assert.False(ok);
        Assert.Contains("未知渠道", msg);
        Assert.Equal(0, db.Queryable<SysNotifyLog>().Count());
    }

    [Fact]
    public async Task NotifyAsync_LongContentAndError_ShouldTruncateInLog()
    {
        var db = CreateDb();
        await SeedRule(db, "JOB_FAIL", threshold: 1);
        await SeedConfig(db, "notify_alert_receivers", "admin@tao.dev");
        var longError = new string('错', 600);
        var email = new FakeEmailSender { Result = (false, longError) };
        var notifier = CreateNotifier(db, email);
        var longContent = new string('告', 1200);

        var sent = await notifier.NotifyAsync("JOB_FAIL", "标题", longContent, 1);

        Assert.Equal(0, sent);
        var log = db.Queryable<SysNotifyLog>().Single();
        Assert.False(log.Success);
        // 内容截断到 1000 字、错误信息截断到 500 字
        Assert.Equal(1000, (log.Content ?? string.Empty).Length);
        Assert.Equal(500, (log.Error ?? string.Empty).Length);
    }

    [Fact]
    public void ParseChannels_ShouldTrimAndDropEmpty()
    {
        Assert.Equal(new[] { "email", "dingtalk" }, AlertNotifier.ParseChannels(" email , dingtalk , "));
        Assert.Empty(AlertNotifier.ParseChannels("  "));
    }
}
