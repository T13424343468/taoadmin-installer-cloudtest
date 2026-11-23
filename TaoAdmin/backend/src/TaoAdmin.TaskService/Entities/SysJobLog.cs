using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.TaskService.Entities;

/// <summary>
/// 任务执行历史（sys_job_log）：每次执行（含重试）落一条，记录时间、结果、耗时与失败原因。
/// </summary>
[SugarTable("sys_job_log", TableDescription = "任务执行历史")]
public class SysJobLog : BaseEntity
{
    /// <summary>任务 ID（sys_job.Id）</summary>
    [SugarColumn(ColumnDescription = "任务ID")]
    public long JobId { get; set; }

    /// <summary>任务名</summary>
    [SugarColumn(Length = 100, ColumnDescription = "任务名")]
    public string JobName { get; set; } = string.Empty;

    /// <summary>任务组</summary>
    [SugarColumn(Length = 50, ColumnDescription = "任务组")]
    public string Group { get; set; } = "default";

    /// <summary>开始时间</summary>
    [SugarColumn(ColumnDescription = "开始时间")]
    public DateTime StartTime { get; set; }

    /// <summary>结束时间</summary>
    [SugarColumn(ColumnDescription = "结束时间")]
    public DateTime EndTime { get; set; }

    /// <summary>耗时（毫秒）</summary>
    [SugarColumn(ColumnDescription = "耗时毫秒")]
    public long DurationMs { get; set; }

    /// <summary>是否成功</summary>
    [SugarColumn(ColumnDescription = "是否成功")]
    public bool Success { get; set; }

    /// <summary>失败原因（成功为空）</summary>
    [SugarColumn(Length = 1000, IsNullable = true, ColumnDescription = "失败原因")]
    public string? Message { get; set; }

    /// <summary>重试序号：0 为首次执行，1 起为第 N 次重试</summary>
    [SugarColumn(ColumnDescription = "重试序号")]
    public int RetryIndex { get; set; }
}
