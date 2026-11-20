namespace TaoAdmin.Application.Dtos;

// ============ 定时任务 DTO（与前端契约对齐） ============

/// <summary>任务列表项</summary>
public class JobItemDto
{
    public string id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string? group { get; set; }
    public string cron { get; set; } = string.Empty;
    /// <summary>处理器标识，如 heartbeatJob</summary>
    public string handler { get; set; } = string.Empty;
    public string? @params { get; set; }
    /// <summary>RUNNING / PAUSED</summary>
    public string status { get; set; } = "RUNNING";
    public string? misfirePolicy { get; set; }
    public string? description { get; set; }
    public string? lastFireTime { get; set; }
    public string? nextFireTime { get; set; }
    public string? lastResult { get; set; }
    public DateTime? createdAt { get; set; }
}

/// <summary>任务保存请求</summary>
public class JobSaveRequest
{
    public string? id { get; set; }
    public string name { get; set; } = string.Empty;
    public string? group { get; set; }
    public string cron { get; set; } = string.Empty;
    public string handler { get; set; } = string.Empty;
    public string? @params { get; set; }
    public string? misfirePolicy { get; set; }
    public string? description { get; set; }
}
