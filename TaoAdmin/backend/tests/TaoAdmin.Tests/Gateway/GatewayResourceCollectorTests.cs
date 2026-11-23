using TaoAdmin.GatewayService;
using Xunit;

namespace TaoAdmin.Tests.Gateway;

/// <summary>
/// 网关本机资源采集器（网关自包含副本）：
/// 真实环境采集返回合法快照、两次采集差分出速率、/proc/meminfo 与 /proc/net/dev 解析、
/// 文件缺失回退、回环网卡排除、字段不足跳过。
/// </summary>
public class GatewayResourceCollectorTests
{
    [Fact]
    public void 构造_真实环境_不抛出()
    {
        var collector = new GatewayResourceCollector();
        Assert.NotNull(collector);
    }

    [Fact]
    public void Collect_真实环境_返回合法区间快照()
    {
        var collector = new GatewayResourceCollector("taoadmin-gateway");
        var snapshot = collector.Collect();
        Assert.Equal("taoadmin-gateway", snapshot.ServiceName);
        Assert.InRange(snapshot.CpuPercent, 0, 100);
        Assert.InRange(snapshot.MemoryPercent, 0, 100);
        Assert.InRange(snapshot.DiskPercent, 0, 100);
        Assert.True(snapshot.NetRxKBps >= 0);
        Assert.True(snapshot.NetTxKBps >= 0);
        // 沙箱为 Linux：/proc/meminfo 可读，总内存应大于 0
        Assert.True(snapshot.MemoryTotalMB > 0);
    }

    [Fact]
    public void Collect_两次采集_速率字段为非负数值()
    {
        var collector = new GatewayResourceCollector();
        collector.Collect();
        var snapshot = collector.Collect();
        Assert.True(snapshot.CpuPercent >= 0);
        Assert.True(snapshot.NetRxKBps >= 0);
        Assert.True(snapshot.NetTxKBps >= 0);
    }

    [Fact]
    public void ReadSystemMemory_真实meminfo_总量大于0()
    {
        var (total, _) = GatewayResourceCollector.ReadSystemMemory();
        Assert.True(total > 0);
    }

    [Fact]
    public void ReadSystemMemory_临时meminfo_按kB换算MB()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-meminfo-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllLines(path, ["MemTotal:       8388608 kB", "MemAvailable:   4194304 kB"]);
            var (total, available) = GatewayResourceCollector.ReadSystemMemory(path);
            Assert.Equal(8192, total);
            Assert.Equal(4096, available);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadSystemMemory_文件不存在_回退GC概览()
    {
        var (total, available) = GatewayResourceCollector.ReadSystemMemory("/nonexistent/meminfo");
        Assert.True(total > 0); // GC 概览在 .NET 下总有可用内存值
        Assert.Equal(0, available);
    }

    [Fact]
    public void ReadSystemMemory_缺MemTotal行_回退GC概览()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-meminfo-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllLines(path, ["MemFree:         1024000 kB"]);
            var (total, _) = GatewayResourceCollector.ReadSystemMemory(path);
            Assert.True(total > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNetworkCounters_真实环境_收发计数非负()
    {
        var (rx, tx) = GatewayResourceCollector.ReadNetworkCounters();
        Assert.True(rx >= 0);
        Assert.True(tx >= 0);
    }

    [Fact]
    public void ReadNetworkCounters_临时dev文件_排除回环并合计收发()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-netdev-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 标准两行表头 + lo（排除）+ eth0（合计）：rx=100 tx=200
            File.WriteAllLines(path,
            [
                "Inter-|   Receive                                                |  Transmit",
                " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed",
                "  lo: 99999  9999 0 0 0 0 0 0 99999  9999 0 0 0 0 0 0",
                "eth0: 100 10 0 0 0 0 0 0 200 20 0 0 0 0 0 0"
            ]);
            var (rx, tx) = GatewayResourceCollector.ReadNetworkCounters(path);
            Assert.Equal(100, rx);
            Assert.Equal(200, tx);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadNetworkCounters_文件不存在_回退网卡枚举()
    {
        var (rx, tx) = GatewayResourceCollector.ReadNetworkCounters("/nonexistent/net/dev");
        Assert.True(rx >= 0);
        Assert.True(tx >= 0);
    }

    [Fact]
    public void ReadNetworkCounters_字段不足的行_跳过不抛出()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-netdev-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllLines(path,
            [
                "header1",
                "header2",
                "  eth0: 100 10 0 0 0 0 0 0" // 字段不足 16 个，整行跳过
            ]);
            var (rx, tx) = GatewayResourceCollector.ReadNetworkCounters(path);
            Assert.Equal(0, rx);
            Assert.Equal(0, tx);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadSystemMemory_MemTotal行无数值_按0处理()
    {
        var path = Path.Combine(Path.GetTempPath(), "gw-meminfo-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllLines(path, ["MemTotal:"]);
            // 无有效 MemTotal → 回退 GC 概览（total > 0）
            var (total, _) = GatewayResourceCollector.ReadSystemMemory(path);
            Assert.True(total > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Collect_全部读数委托抛错_按0降级不抛出()
    {
        var collector = new GatewayResourceCollector(
            "taoadmin-gateway",
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException());
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.CpuPercent);
        Assert.Equal(0, snapshot.MemoryPercent);
        Assert.Equal(0, snapshot.DiskPercent);
        Assert.Equal(0, snapshot.NetRxKBps);
        Assert.Equal(0, snapshot.NetTxKBps);
    }

    [Fact]
    public void Collect_内存读数委托抛错_仅内存按0其余正常()
    {
        var collector = new GatewayResourceCollector(
            "taoadmin-gateway",
            () => TimeSpan.FromSeconds(1),
            () => throw new InvalidOperationException(),
            () => (10.0, 100.0),
            () => (0L, 0L));
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.MemoryPercent);
        Assert.Equal(10, snapshot.DiskPercent);
    }

    [Fact]
    public void Collect_磁盘读数委托抛错_仅磁盘按0其余正常()
    {
        var collector = new GatewayResourceCollector(
            "taoadmin-gateway",
            () => TimeSpan.FromSeconds(1),
            () => (8192.0, 4096.0),
            () => throw new InvalidOperationException(),
            () => (0L, 0L));
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.DiskPercent);
        Assert.Equal(50, snapshot.MemoryPercent);
    }

    [Fact]
    public void Collect_网络读数委托抛错_仅网络按0其余正常()
    {
        var collector = new GatewayResourceCollector(
            "taoadmin-gateway",
            () => TimeSpan.FromSeconds(1),
            () => (8192.0, 4096.0),
            () => (10.0, 100.0),
            () => throw new InvalidOperationException());
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.NetRxKBps);
        Assert.Equal(0, snapshot.NetTxKBps);
        Assert.Equal(10, snapshot.DiskPercent);
    }

    [Fact]
    public void Collect_注入可控读数_差分出CPU与网络速率()
    {
        // 10 秒内 CPU 时间增加 10 秒、4 核 → 25%；接收 102400 字节 → 10 KB/s
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var now = t0;
        var cpu = TimeSpan.FromSeconds(10);
        var rx = 0L;
        var collector = new GatewayResourceCollector(
            "taoadmin-gateway",
            () => cpu,
            () => (8192.0, 4096.0),
            () => (10.0, 100.0),
            () => (rx, 0L),
            () => now,
            processorCount: 4);
        collector.Collect();
        now = t0.AddSeconds(10);
        cpu = TimeSpan.FromSeconds(20);
        rx = 102400;
        var snapshot = collector.Collect();
        Assert.Equal(25, snapshot.CpuPercent);
        Assert.Equal(10, snapshot.NetRxKBps);
        Assert.Equal(50, snapshot.MemoryPercent);
        Assert.Equal(10, snapshot.DiskPercent);
    }

    [Fact]
    public void Collect_时钟未推进_速率为0()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var cpu = TimeSpan.FromSeconds(10);
        var collector = new GatewayResourceCollector(
            "taoadmin-gateway",
            () => cpu,
            () => (8192.0, 4096.0),
            () => (10.0, 100.0),
            () => (0L, 0L),
            () => t0);
        collector.Collect();
        cpu = TimeSpan.FromSeconds(20);
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.CpuPercent);
        Assert.Equal(0, snapshot.NetRxKBps);
        Assert.Equal(0, snapshot.NetTxKBps);
    }
}
