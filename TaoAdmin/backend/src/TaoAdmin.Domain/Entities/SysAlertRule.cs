using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 告警规则（sys_alert_rule）：定义哪些事件触发告警、连续失败阈值与通知渠道。
/// 事件类型：SERVICE_DOWN（服务健康探测失败）/ JOB_FAIL（定时任务连续失败）。
/// 触发口径：同类事件连续发生次数达到 Threshold 时触发一次（恢复后重新计数）。
/// </summary>
[SugarTable("sys_alert_rule", TableDescription = "告警规则")]
public class SysAlertRule : BaseEntity
{
    /// <summary>规则名</summary>
    [SugarColumn(Length = 100, ColumnDescription = "规则名")]
    public string Name { get; set; } = string.Empty;

    /// <summary>事件类型：SERVICE_DOWN / JOB_FAIL</summary>
    [SugarColumn(Length = 30, ColumnDescription = "事件类型")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>连续失败阈值：达到该次数触发告警（最小 1）</summary>
    [SugarColumn(ColumnDescription = "连续失败阈值")]
    public int Threshold { get; set; } = 1;

    /// <summary>通知渠道（逗号分隔）：email / dingtalk</summary>
    [SugarColumn(Length = 100, ColumnDescription = "通知渠道")]
    public string Channels { get; set; } = "email";

    /// <summary>是否启用</summary>
    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    /// <summary>备注</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
