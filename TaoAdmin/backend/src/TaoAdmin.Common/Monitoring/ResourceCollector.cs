namespace TaoAdmin.Common.Monitoring;

/// <summary>
/// 资源采集器：把两次原始读数差分成速率快照（CPU%、内存、磁盘、网络 KB/s）。
/// 首次采集无前值，速率为 0；计数器回退（如进程重启）按 0 处理，不产生负速率。
/// 线程安全：采集可来自监控页实时请求与后台采样并发调用。
/// </summary>
public class ResourceCollector
{
    private readonly string _serviceName;
    private readonly IResourceSource _source;
    private readonly object _lock = new();
    private ResourceCounters? _prev;

    public ResourceCollector(string serviceName, IResourceSource source)
    {
        _serviceName = serviceName;
        _source = source;
    }

    /// <summary>采集一次并差分出速率快照。</summary>
    public ResourceSnapshot Collect()
    {
        var cur = _source.Read();
        ResourceCounters? prev;
        lock (_lock)
        {
            prev = _prev;
            _prev = cur;
        }

        double cpu = 0, rxKbps = 0, txKbps = 0;
        if (prev != null)
        {
            var seconds = (cur.At - prev.At).TotalSeconds;
            if (seconds > 0)
            {
                // 进程 CPU 时间差 / 时长 / 核心数 = 占整机容量的比例（0~100）
                var cpuRatio = (cur.ProcessCpuTime - prev.ProcessCpuTime).TotalSeconds / seconds;
                cpu = ClampPercent(cpuRatio / cur.ProcessorCount * 100);
                rxKbps = ClampRate((cur.NetRxBytes - prev.NetRxBytes) / seconds / 1024.0);
                txKbps = ClampRate((cur.NetTxBytes - prev.NetTxBytes) / seconds / 1024.0);
            }
        }

        var memPercent = cur.SystemMemoryTotalMB > 0
            ? ClampPercent(cur.ProcessMemoryMB / cur.SystemMemoryTotalMB * 100)
            : 0;
        var diskPercent = cur.DiskTotalGB > 0
            ? ClampPercent(cur.DiskUsedGB / cur.DiskTotalGB * 100)
            : 0;

        return new ResourceSnapshot
        {
            ServiceName = _serviceName,
            CollectedAt = cur.At,
            CpuPercent = cpu,
            MemoryUsedMB = Math.Round(cur.ProcessMemoryMB, 1),
            MemoryTotalMB = Math.Round(cur.SystemMemoryTotalMB, 1),
            MemoryPercent = memPercent,
            DiskUsedGB = Math.Round(cur.DiskUsedGB, 2),
            DiskTotalGB = Math.Round(cur.DiskTotalGB, 2),
            DiskPercent = diskPercent,
            NetRxKBps = Math.Round(rxKbps, 1),
            NetTxKBps = Math.Round(txKbps, 1)
        };
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    private static double ClampRate(double value) => value < 0 ? 0 : value;
}
