using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.TaskService.Entities;

/// <summary>定时任务定义（sys_job）</summary>
[SugarTable("sys_job", TableDescription = "定时任务")]
public class SysJob : BaseEntity
{
    /// <summary>任务名（Quartz JobKey.Name）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "任务名")]
    public string Name { get; set; } = string.Empty;

    /// <summary>任务组</summary>
    [SugarColumn(Length = 50, ColumnDescription = "任务组")]
    public string Group { get; set; } = "default";

    /// <summary>Cron 表达式</summary>
    [SugarColumn(Length = 50, ColumnDescription = "Cron 表达式")]
    public string Cron { get; set; } = string.Empty;

    /// <summary>处理器标识（映射到具体 IJob 实现）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "处理器")]
    public string Handler { get; set; } = string.Empty;

    /// <summary>任务参数（JSON）</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "任务参数")]
    public string? Params { get; set; }

    /// <summary>状态：RUNNING / PAUSED</summary>
    [SugarColumn(Length = 20, ColumnDescription = "状态")]
    public string Status { get; set; } = "RUNNING";

    /// <summary>备注</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "备注")]
    public string? Description { get; set; }

    /// <summary>上次执行时间</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "上次执行时间")]
    public DateTime? LastFireTime { get; set; }

    /// <summary>下次执行时间</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "下次执行时间")]
    public DateTime? NextFireTime { get; set; }

    /// <summary>上次执行结果</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "上次执行结果")]
    public string? LastResult { get; set; }

    /// <summary>失败自动重试次数（任务级配置，0 表示不重试）</summary>
    [SugarColumn(ColumnDescription = "失败自动重试次数", IsNullable = true)]
    public int RetryCount { get; set; }
}
