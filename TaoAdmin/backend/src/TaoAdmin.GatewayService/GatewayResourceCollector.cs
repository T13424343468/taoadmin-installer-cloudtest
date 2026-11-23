using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TaoAdmin.GatewayService;

/// <summary>
/// 网关本机资源采集器（②）：网关保持独立部署（不引用 Common），与各服务 /metrics/resources
/// 的 JSON 结构对齐，采集本进程 CPU/内存/磁盘/网络。CPU 与网络为两次采样差分出的速率，
/// 单项采集失败按 0 处理，不抛异常。
/// </summary>
public class GatewayResourceCollector
{
    private readonly string _serviceName;
    private readonly Func<TimeSpan> _readCpuTime;
    private readonly Func<(double TotalMB, double AvailableMB)> _readSystemMemory;
    private readonly Func<(double Used, double Total)> _readDisk;
    private readonly Func<(long Rx, long Tx)> _readNetwork;
    private readonly int _processorCount;
    private readonly object _lock = new();
    private readonly Func<DateTime> _utcNow;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuAt;
    private long _lastRxBytes;
    private long _lastTxBytes;
    private DateTime _lastNetAt;

    public GatewayResourceCollector(string serviceName = "taoadmin-gateway")
        : this(serviceName,
            () => Process.GetCurrentProcess().TotalProcessorTime,
            () => ReadSystemMemory(),
            () => ReadDisk(),
            () => ReadNetworkCounters())
    {
    }

    /// <summary>测试构造：注入各读取委托，可强制抛错覆盖降级分支；utcNow 可控时钟用于差分断言。</summary>
    internal GatewayResourceCollector(
        string serviceName,
        Func<TimeSpan> readCpuTime,
        Func<(double TotalMB, double AvailableMB)> readSystemMemory,
        Func<(double Used, double Total)> readDisk,
        Func<(long Rx, long Tx)> readNetwork,
        Func<DateTime>? utcNow = null,
        int? processorCount = null)
    {
        _serviceName = serviceName;
        _readCpuTime = readCpuTime;
        _readSystemMemory = readSystemMemory;
        _readDisk = readDisk;
        _readNetwork = readNetwork;
        _processorCount = processorCount ?? Environment.ProcessorCount;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _lastCpuTime = TryRead(readCpuTime, TimeSpan.Zero);
        _lastCpuAt = _utcNow();
        _lastNetAt = _utcNow();
        (_lastRxBytes, _lastTxBytes) = TryRead(readNetwork, (0L, 0L));
    }

    /// <summary>采集一次本进程资源快照。</summary>
    public GatewayResourceSnapshot Collect()
    {
        var snapshot = new GatewayResourceSnapshot
        {
            ServiceName = _serviceName,
            CollectedAt = DateTime.Now
        };

        try
        {
            var cpuTime = _readCpuTime();
            var now = _utcNow();
            var elapsed = (now - _lastCpuAt).TotalSeconds;
            lock (_lock)
            {
                snapshot.CpuPercent = elapsed > 0
                    ? Math.Clamp((cpuTime - _lastCpuTime).TotalSeconds / elapsed / _processorCount * 100, 0, 100)
                    : 0;
                _lastCpuTime = cpuTime;
                _lastCpuAt = now;
            }
        }
        catch
        {
            snapshot.CpuPercent = 0;
        }

        try
        {
            var (total, available) = _readSystemMemory();
            snapshot.MemoryTotalMB = total;
            snapshot.MemoryUsedMB = total - available;
            snapshot.MemoryPercent = total > 0 ? Math.Round((total - available) / total * 100, 1) : 0;
        }
        catch
        {
            snapshot.MemoryPercent = 0;
        }

        try
        {
            var (used, total) = _readDisk();
            snapshot.DiskTotalGB = Math.Round(total, 2);
            snapshot.DiskUsedGB = Math.Round(used, 2);
            snapshot.DiskPercent = total > 0 ? Math.Round(used / total * 100, 1) : 0;
        }
        catch
        {
            snapshot.DiskPercent = 0;
        }

        try
        {
            var now = _utcNow();
            var (rx, tx) = _readNetwork();
            var elapsed = (now - _lastNetAt).TotalSeconds;
            lock (_lock)
            {
                snapshot.NetRxKBps = elapsed > 0 ? Math.Max((rx - _lastRxBytes) / elapsed / 1024.0, 0) : 0;
                snapshot.NetTxKBps = elapsed > 0 ? Math.Max((tx - _lastTxBytes) / elapsed / 1024.0, 0) : 0;
                _lastRxBytes = rx;
                _lastTxBytes = tx;
                _lastNetAt = now;
            }
        }
        catch
        {
            snapshot.NetRxKBps = 0;
            snapshot.NetTxKBps = 0;
        }

        return snapshot;
    }

    private static T TryRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>程序所在磁盘用量（GB）：根目录所在盘；读不到返回 (0, 0)。</summary>
    internal static (double Used, double Total) ReadDisk()
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory) ?? "/";
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.TotalSize == 0)
            return (0, 0);
        return ((drive.TotalSize - drive.AvailableFreeSpace) / 1073741824.0, drive.TotalSize / 1073741824.0);
    }

    /// <summary>系统内存（MB）：Linux 读 /proc/meminfo 的 MemTotal/MemAvailable；读不到回退 GC 概览。</summary>
    internal static (double TotalMB, double AvailableMB) ReadSystemMemory(string? path = null)
    {
        path ??= "/proc/meminfo";
        if (File.Exists(path))
        {
            double total = 0, available = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    total = ParseKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    available = ParseKb(line);
            }

            if (total > 0)
                return (total / 1024.0, available / 1024.0);
        }
        return (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576.0, 0);
    }

    private static double ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && double.TryParse(parts[1], out var value) ? value : 0;
    }

    /// <summary>非回环网卡收发字节合计：Linux 优先读 /proc/net/dev（排除 lo），读不到回退 NetworkInterface。</summary>
    internal static (long Rx, long Tx) ReadNetworkCounters(string? path = null)
    {
        path ??= "/proc/net/dev";
        if (File.Exists(path))
        {
            long rx = 0, tx = 0;
            foreach (var line in File.ReadLines(path).Skip(2))
            {
                var sep = line.IndexOf(':');
                if (sep <= 0)
                    continue;
                var name = line[..sep].Trim();
                if (name == "lo")
                    continue;
                var fields = line[(sep + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 16
                    && long.TryParse(fields[0], out var ifRx)
                    && long.TryParse(fields[8], out var ifTx))
                {
                    rx += ifRx;
                    tx += ifTx;
                }
            }
            return (rx, tx);
        }

        long fallbackRx = 0, fallbackTx = 0;
        if (!NetworkInterface.GetIsNetworkAvailable())
            return (0, 0);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            var stats = nic.GetIPStatistics();
            fallbackRx += stats.BytesReceived;
            fallbackTx += stats.BytesSent;
        }
        return (fallbackRx, fallbackTx);
    }
}
