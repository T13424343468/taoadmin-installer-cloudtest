using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 告警规则应用服务测试（SQLite 内存库真库）：
/// 校验（名称/事件类型/阈值/渠道）、新增、更新、删除、清单。
/// </summary>
public class AlertRuleAppServiceTests
{
    private static (SqlSugarClient Db, AlertRuleAppService Svc) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysAlertRule>();
        return (db, new AlertRuleAppService(db, new SnowflakeIdGenerator(1, 1)));
    }

    private static AlertRuleSaveRequest ValidRequest(string? name = "任务失败告警", string eventType = "JOB_FAIL",
        int threshold = 3, string channels = "email,dingtalk")
        => new() { Name = name!, EventType = eventType, Threshold = threshold, Channels = channels, Enabled = true };

    [Fact]
    public async Task SaveAsync_NewRule_ShouldInsert()
    {
        var (db, svc) = Create();

        await svc.SaveAsync(ValidRequest());

        var row = db.Queryable<SysAlertRule>().Single();
        Assert.Equal("任务失败告警", row.Name);
        Assert.Equal("JOB_FAIL", row.EventType);
        Assert.Equal(3, row.Threshold);
        Assert.Equal("email,dingtalk", row.Channels);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task SaveAsync_ExistingId_ShouldUpdateInPlace()
    {
        var (db, svc) = Create();
        await svc.SaveAsync(ValidRequest());
        var id = db.Queryable<SysAlertRule>().Single().Id;

        await svc.SaveAsync(new AlertRuleSaveRequest
        {
            Id = id, Name = "服务下线告警", EventType = "SERVICE_DOWN", Threshold = 2,
            Channels = "dingtalk", Enabled = false, Remark = "备注"
        });

        var row = db.Queryable<SysAlertRule>().Single();
        Assert.Equal(id, row.Id);
        Assert.Equal("服务下线告警", row.Name);
        Assert.Equal("SERVICE_DOWN", row.EventType);
        Assert.Equal(2, row.Threshold);
        Assert.Equal("dingtalk", row.Channels);
        Assert.False(row.Enabled);
        Assert.Equal("备注", row.Remark);
    }

    [Fact]
    public async Task SaveAsync_NonExistingId_ShouldThrow()
    {
        var (_, svc) = Create();
        var req = ValidRequest();
        req.Id = 12345;

        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(req));
        Assert.Equal("告警规则不存在", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_BlankName_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(ValidRequest(name: "  ")));
        Assert.Equal("规则名不能为空", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_InvalidEventType_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(ValidRequest(eventType: "DISK_FULL")));
        Assert.Contains("事件类型不合法", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_ZeroThreshold_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(ValidRequest(threshold: 0)));
        Assert.Equal("连续失败阈值至少为 1", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_NegativeThreshold_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(ValidRequest(threshold: -1)));
        Assert.Equal("连续失败阈值至少为 1", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_EmptyChannels_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(ValidRequest(channels: " , ")));
        Assert.Equal("通知渠道不能为空", ex.Message);
    }

    [Fact]
    public async Task SaveAsync_UnknownChannel_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(ValidRequest(channels: "email,sms")));
        Assert.Contains("通知渠道不合法", ex.Message);
    }

    [Fact]
    public async Task ListAsync_ShouldReturnRulesOrderedByCreatedDesc()
    {
        var (db, svc) = Create();
        await svc.SaveAsync(ValidRequest(name: "第一条"));
        await svc.SaveAsync(ValidRequest(name: "第二条"));

        var list = await svc.ListAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("第二条", list[0].Name);
        Assert.Equal("第一条", list[1].Name);
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDelete_AndHideFromList()
    {
        var (db, svc) = Create();
        await svc.SaveAsync(ValidRequest());
        var id = db.Queryable<SysAlertRule>().Single().Id;

        await svc.DeleteAsync(id);

        var row = db.Queryable<SysAlertRule>().Single();
        Assert.True(row.IsDeleted);
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task DeleteAsync_NonExisting_ShouldThrow()
    {
        var (_, svc) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.DeleteAsync(999));
        Assert.Equal("告警规则不存在", ex.Message);
    }
}
