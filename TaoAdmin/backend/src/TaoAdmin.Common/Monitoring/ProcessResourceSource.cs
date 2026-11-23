namespace TaoAdmin.Common.Monitoring;

using System.Diagnostics;
using System.Net.NetworkInformation;

/// <summary>资源计数器读取源：屏蔽进程/系统/磁盘/网络的真实读取方式，测试可替换。</summary>
public interface IResourceSource
{
    /// <summary>读一次原始计数器。</summary>
    ResourceCounters Read();
}

/// <summary>
/// 真实资源读取源：进程 CPU 时间与工作集、系统总内存（Linux /proc/meminfo）、
/// 程序所在磁盘用量、非回环网卡收发字节。单次读数失败按 0 处理，不阻断采样。
/// </summary>
public class ProcessResourceSource : IResourceSource
{
    private readonly Func<TimeSpan> _readCpuTime;
    private readonly Func<double> _readProcessMemory;
    private readonly Func<double> _readSystemMemoryTotal;
    private readonly Func<(double Used, double Total)> _readDisk;
    private readonly Func<(long Rx, long Tx)> _readNetwork;

    public ProcessResourceSource()
        : this(ReadCpuTime, ReadProcessMemory, () => ReadSystemMemoryTotal(null), () => ReadDisk(null), ReadNetwork)
    {
    }

    /// <summary>测试构造：注入各读取委托，可强制抛错覆盖降级分支。</summary>
    internal ProcessResourceSource(
        Func<TimeSpan> readCpuTime,
        Func<double> readProcessMemory,
        Func<double> readSystemMemoryTotal,
        Func<(double Used, double Total)> readDisk,
        Func<(long Rx, long Tx)> readNetwork)
    {
        _readCpuTime = readCpuTime;
        _readProcessMemory = readProcessMemory;
        _readSystemMemoryTotal = readSystemMemoryTotal;
        _readDisk = readDisk;
        _readNetwork = readNetwork;
    }

    public ResourceCounters Read()
    {
        // 单项读数失败按 0 处理：资源采样是旁路观测，不能因个别系统接口异常中断
        double mem = TryRead(() => _readProcessMemory());
        double memTotal = TryRead(_readSystemMemoryTotal);
        var disk = TryRead(_readDisk);
        var net = TryRead(_readNetwork);
        var cpu = TryRead(() => _readCpuTime());
        return new ResourceCounters(
            DateTime.Now, cpu, mem, memTotal, disk.Used, disk.Total, net.Rx, net.Tx,
            Environment.ProcessorCount);
    }

    private static T TryRead<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return default!;
        }
    }

    private static TimeSpan ReadCpuTime() => Process.GetCurrentProcess().TotalProcessorTime;

    private static double ReadProcessMemory() => Environment.WorkingSet / 1048576.0;

    /// <summary>系统总内存（MB）：Linux 读 /proc/meminfo 的 MemTotal；读不到返回 0（未知）。</summary>
    internal static double ReadSystemMemoryTotal(string? path = null)
    {
        path ??= "/proc/meminfo";
        if (!File.Exists(path)) return 0;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("MemTotal:", StringComparison.Ordinal)) continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && double.TryParse(parts[1], out var kb) ? kb / 1024.0 : 0;
        }
        return 0;
    }

    /// <summary>程序所在磁盘用量（GB）：根目录所在盘；读不到返回 (0, 0)。</summary>
    internal static (double Used, double Total) ReadDisk(string? root = null)
    {
        var drive = new DriveInfo(root ?? AppContext.BaseDirectory);
        if (!drive.IsReady || drive.TotalSize == 0) return (0, 0);
        return ((drive.TotalSize - drive.AvailableFreeSpace) / 1073741824.0, drive.TotalSize / 1073741824.0);
    }

    /// <summary>非回环网卡收发字节合计：无可用网卡返回 (0, 0)。</summary>
    internal static (long Rx, long Tx) ReadNetwork()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        long rx = 0, tx = 0;
        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            var stats = nic.GetIPv4Statistics();
            rx += stats.BytesReceived;
            tx += stats.BytesSent;
        }
        return (rx, tx);
    }
}
