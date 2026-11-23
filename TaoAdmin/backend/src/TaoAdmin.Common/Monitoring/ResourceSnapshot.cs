namespace TaoAdmin.Common.Monitoring;

/// <summary>
/// 资源指标快照（单服务单时刻）：CPU / 内存 / 磁盘 / 网络。
/// 各服务经 /metrics/resources 暴露，网关聚合，监控中心后台任务定时采样留存趋势。
/// </summary>
public class ResourceSnapshot
{
    /// <summary>服务名（如 taoadmin-gateway）</summary>
    public string ServiceName { get; set; } = "";

    /// <summary>采样时间</summary>
    public DateTime CollectedAt { get; set; }

    /// <summary>进程 CPU 占用率（0~100，按整机核心数归一）</summary>
    public double CpuPercent { get; set; }

    /// <summary>进程内存占用（MB）</summary>
    public double MemoryUsedMB { get; set; }

    /// <summary>系统总内存（MB；读不到为 0）</summary>
    public double MemoryTotalMB { get; set; }

    /// <summary>进程内存占系统总内存比例（0~100；总内存未知为 0）</summary>
    public double MemoryPercent { get; set; }

    /// <summary>所在磁盘已用空间（GB）</summary>
    public double DiskUsedGB { get; set; }

    /// <summary>所在磁盘总容量（GB）</summary>
    public double DiskTotalGB { get; set; }

    /// <summary>磁盘使用率（0~100）</summary>
    public double DiskPercent { get; set; }

    /// <summary>网络接收速率（KB/s，全部非回环网卡合计）</summary>
    public double NetRxKBps { get; set; }

    /// <summary>网络发送速率（KB/s，全部非回环网卡合计）</summary>
    public double NetTxKBps { get; set; }

    /// <summary>指标是否可用（聚合方在目标服务探测失败时置 false）</summary>
    public bool Available { get; set; } = true;
}

/// <summary>原始资源计数器（采集源的一次读数；速率由两次读数差分得出）</summary>
public record ResourceCounters(
    DateTime At,
    TimeSpan ProcessCpuTime,
    double ProcessMemoryMB,
    double SystemMemoryTotalMB,
    double DiskUsedGB,
    double DiskTotalGB,
    long NetRxBytes,
    long NetTxBytes,
    int ProcessorCount);
