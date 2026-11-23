using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 通知渠道配置测试：新键（SMTP 密码/钉钉 Webhook/告警接收人）白名单读写。
/// </summary>
public class NotifyChannelTests
{
    private static (SqlSugarClient Db, NotifyChannelAppService Svc) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        return (db, new NotifyChannelAppService(db, new SnowflakeIdGenerator(1, 1)));
    }

    [Fact]
    public async Task GetAsync_EmptyTable_ShouldReturnAllKnownKeysEmpty()
    {
        var (_, svc) = Create();

        var config = await svc.GetAsync();

        Assert.Equal(NotifyChannelAppService.KnownKeys.Length, config.Count);
        Assert.Contains("notify_smtp_pass", config.Keys);
        Assert.Contains("notify_dingtalk_webhook", config.Keys);
        Assert.Contains("notify_alert_receivers", config.Keys);
        Assert.All(config.Values, v => Assert.Equal(string.Empty, v));
    }

    [Fact]
    public async Task SaveAsync_WithNewKeys_ShouldPersistAndReadBack()
    {
        var (db, svc) = Create();

        await svc.SaveAsync(new Dictionary<string, string>
        {
            ["notify_smtp_pass"] = "auth-code-123",
            ["notify_dingtalk_webhook"] = "https://oapi.dingtalk.com/robot/send?access_token=x",
            ["notify_alert_receivers"] = "admin@tao.dev"
        });

        var config = await svc.GetAsync();
        Assert.Equal("auth-code-123", config["notify_smtp_pass"]);
        Assert.Equal("https://oapi.dingtalk.com/robot/send?access_token=x", config["notify_dingtalk_webhook"]);
        Assert.Equal("admin@tao.dev", config["notify_alert_receivers"]);
        // 保存不产生重复行
        Assert.Equal(3, db.Queryable<SysSiteConfig>().Count());
    }

    [Fact]
    public async Task GetAsync_NotifyKeyNotInWhitelist_ShouldIgnore()
    {
        var db = Create();
        await db.Db.Insertable(new SysSiteConfig
        {
            Id = 9100, ConfigKey = "notify_secret_extra", ConfigValue = "should-not-leak"
        }).ExecuteCommandAsync();
        // 已知键但值为 NULL：读回应退为空串
        await db.Db.Insertable(new SysSiteConfig
        {
            Id = 9101, ConfigKey = "notify_smtp_host", ConfigValue = null
        }).ExecuteCommandAsync();

        var config = await db.Svc.GetAsync();

        // 白名单外的 notify_ 键不透出，NULL 值回退空串
        Assert.False(config.ContainsKey("notify_secret_extra"));
        Assert.Equal(NotifyChannelAppService.KnownKeys.Length, config.Count);
        Assert.Equal(string.Empty, config["notify_smtp_host"]);
    }

    [Fact]
    public async Task SaveAsync_Twice_ShouldUpdateExistingRow()
    {
        var (db, svc) = Create();

        await svc.SaveAsync(new Dictionary<string, string> { ["notify_alert_receivers"] = "first@tao.dev" });
        await svc.SaveAsync(new Dictionary<string, string> { ["notify_alert_receivers"] = "second@tao.dev" });

        var config = await svc.GetAsync();
        Assert.Equal("second@tao.dev", config["notify_alert_receivers"]);
        // 第二次保存走更新而非新增，不产生重复行
        Assert.Equal(1, db.Queryable<SysSiteConfig>().Count());
    }

    [Fact]
    public async Task SaveAsync_UnknownKey_ShouldReject()
    {
        var (_, svc) = Create();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new Dictionary<string, string> { ["hacker_key"] = "x" }));
        Assert.Contains("没有可保存的通知渠道配置项", ex.Message);
    }
}
