using SqlSugar;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>告警规则保存请求</summary>
public class AlertRuleSaveRequest
{
    public long? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>事件类型：SERVICE_DOWN / JOB_FAIL</summary>
    public string EventType { get; set; } = string.Empty;
    /// <summary>连续失败阈值（最小 1）</summary>
    public int Threshold { get; set; } = 1;
    /// <summary>通知渠道（逗号分隔）：email / dingtalk</summary>
    public string Channels { get; set; } = "email";
    public bool Enabled { get; set; } = true;
    public string? Remark { get; set; }
}

/// <summary>
/// 告警规则应用服务：规则 CRUD 与校验。规则决定哪些事件触发告警、连续失败阈值与通知渠道。
/// </summary>
public class AlertRuleAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
{
    /// <summary>支持的事件类型</summary>
    public static readonly string[] KnownEventTypes = { "SERVICE_DOWN", "JOB_FAIL" };

    /// <summary>规则清单（全量，规则数量有限）</summary>
    public async Task<List<SysAlertRule>> ListAsync()
        => await db.Queryable<SysAlertRule>().Where(r => r.IsDeleted == false)
            .OrderByDescending(r => r.CreatedAt).ToListAsync();

    /// <summary>新增或更新规则：校验名称、事件类型、阈值与渠道合法性。</summary>
    public async Task SaveAsync(AlertRuleSaveRequest req)
    {
        Validate(req);
        if (req.Id is long id)
        {
            var row = await db.Queryable<SysAlertRule>().FirstAsync(r => r.Id == id && r.IsDeleted == false)
                ?? throw new DomainException("告警规则不存在", 404);
            row.Name = req.Name;
            row.EventType = req.EventType;
            row.Threshold = req.Threshold;
            row.Channels = req.Channels;
            row.Enabled = req.Enabled;
            row.Remark = req.Remark;
            row.UpdatedAt = DateTime.Now;
            await db.Updateable(row).ExecuteCommandAsync();
            return;
        }
        await db.Insertable(new SysAlertRule
        {
            Id = snowflake.NextId(),
            Name = req.Name,
            EventType = req.EventType,
            Threshold = req.Threshold,
            Channels = req.Channels,
            Enabled = req.Enabled,
            Remark = req.Remark
        }).ExecuteCommandAsync();
    }

    /// <summary>删除规则（软删除）</summary>
    public async Task DeleteAsync(long id)
    {
        var row = await db.Queryable<SysAlertRule>().FirstAsync(r => r.Id == id && r.IsDeleted == false)
            ?? throw new DomainException("告警规则不存在", 404);
        row.IsDeleted = true;
        row.UpdatedAt = DateTime.Now;
        await db.Updateable(row).ExecuteCommandAsync();
    }

    private static void Validate(AlertRuleSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new DomainException("规则名不能为空", 400);
        if (!KnownEventTypes.Contains(req.EventType))
            throw new DomainException("事件类型不合法（仅支持 SERVICE_DOWN / JOB_FAIL）", 400);
        if (req.Threshold < 1)
            throw new DomainException("连续失败阈值至少为 1", 400);
        var channels = AlertNotifier.ParseChannels(req.Channels);
        if (channels.Length == 0)
            throw new DomainException("通知渠道不能为空", 400);
        if (channels.Any(c => !AlertNotifier.KnownChannels.Contains(c)))
            throw new DomainException("通知渠道不合法（仅支持 email / dingtalk）", 400);
    }
}
