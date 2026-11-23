using TaoAdmin.Common.Alerting;
using TaoAdmin.Common.Monitoring;
using Xunit;

namespace TaoAdmin.Tests.Common;

/// <summary>
/// 资源采集器差分逻辑（注入式假数据源 + 可控时间）：
/// 首采速率 0、两次采样差分出 CPU/网络速率、计数器回退钳 0、时长为 0 速率为 0、
/// 内存总量未知百分比为 0、磁盘百分比计算。
/// </summary>
public class ResourceCollectorTests
{
    /// <summary>假数据源：按脚本逐次吐出计数器。</summary>
    private sealed class FakeSource(params ResourceCounters[] readings) : IResourceSource
    {
        private int _index;
        public ResourceCounters Read() => readings[Math.Min(_index++, readings.Length - 1)];
    }

    private static ResourceCounters Counters(
        DateTime at, TimeSpan cpu, double memMB = 512, double memTotalMB = 8192,
        double diskUsedGB = 40, double diskTotalGB = 100,
        long rx = 0, long tx = 0, int cores = 4)
        => new(at, cpu, memMB, memTotalMB, diskUsedGB, diskTotalGB, rx, tx, cores);

    [Fact]
    public void Collect_首次采集_速率为0()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        var collector = new ResourceCollector("taoadmin-test",
            new FakeSource(Counters(at, TimeSpan.FromSeconds(10), rx: 1024, tx: 2048)));
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.CpuPercent);
        Assert.Equal(0, snapshot.NetRxKBps);
        Assert.Equal(0, snapshot.NetTxKBps);
        // 绝对量照常上报
        Assert.Equal(512, snapshot.MemoryUsedMB);
        Assert.Equal(8192, snapshot.MemoryTotalMB);
    }

    [Fact]
    public void Collect_两次采样_差分出CPU与网络速率()
    {
        // 10 秒内进程 CPU 时间增加 10 秒、4 核 → 10/10/4 = 25%
        // 10 秒内接收 102400 字节 → 10 KB/s
        var t1 = new DateTime(2026, 1, 1, 12, 0, 0);
        var t2 = t1.AddSeconds(10);
        var collector = new ResourceCollector("taoadmin-test", new FakeSource(
            Counters(t1, TimeSpan.FromSeconds(10), rx: 0, tx: 0),
            Counters(t2, TimeSpan.FromSeconds(20), rx: 102400, tx: 51200)));
        collector.Collect();
        var snapshot = collector.Collect();
        Assert.Equal(25, snapshot.CpuPercent);
        Assert.Equal(10, snapshot.NetRxKBps);
        Assert.Equal(5, snapshot.NetTxKBps);
    }

    [Fact]
    public void Collect_计数器回退_速率钳0不产生负值()
    {
        var t1 = new DateTime(2026, 1, 1, 12, 0, 0);
        var t2 = t1.AddSeconds(10);
        var collector = new ResourceCollector("taoadmin-test", new FakeSource(
            Counters(t1, TimeSpan.FromSeconds(100), rx: 999999, tx: 999999),
            Counters(t2, TimeSpan.FromSeconds(1), rx: 1, tx: 1)));
        collector.Collect();
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.CpuPercent);
        Assert.Equal(0, snapshot.NetRxKBps);
        Assert.Equal(0, snapshot.NetTxKBps);
    }

    [Fact]
    public void Collect_两次采样时长为0_速率为0()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        var collector = new ResourceCollector("taoadmin-test", new FakeSource(
            Counters(at, TimeSpan.FromSeconds(10)),
            Counters(at, TimeSpan.FromSeconds(20), rx: 102400, tx: 102400)));
        collector.Collect();
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.CpuPercent);
        Assert.Equal(0, snapshot.NetRxKBps);
        Assert.Equal(0, snapshot.NetTxKBps);
    }

    [Fact]
    public void Collect_内存总量未知_内存百分比为0()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        var collector = new ResourceCollector("taoadmin-test",
            new FakeSource(Counters(at, TimeSpan.FromSeconds(10), memMB: 512, memTotalMB: 0)));
        var snapshot = collector.Collect();
        Assert.Equal(0, snapshot.MemoryPercent);
        Assert.Equal(512, snapshot.MemoryUsedMB);
    }

    [Fact]
    public void Collect_内存与磁盘百分比_按总量归一()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        var collector = new ResourceCollector("taoadmin-test", new FakeSource(
            Counters(at, TimeSpan.FromSeconds(10), memMB: 4096, memTotalMB: 8192, diskUsedGB: 25, diskTotalGB: 100)));
        var snapshot = collector.Collect();
        Assert.Equal(50, snapshot.MemoryPercent);
        Assert.Equal(25, snapshot.DiskPercent);
        Assert.Equal(100, snapshot.DiskTotalGB);
        Assert.Equal(25, snapshot.DiskUsedGB);
    }

    [Fact]
    public void Collect_并发采集_不抛异常且状态一致()
    {
        var at = new DateTime(2026, 1, 1, 12, 0, 0);
        var collector = new ResourceCollector("taoadmin-test",
            new FakeSource(Counters(at, TimeSpan.FromSeconds(10))));
        Parallel.For(0, 32, _ => collector.Collect());
    }
}

/// <summary>
/// 真实资源读取源（注入委托）：单项读数抛错按默认值处理不中断采样；
/// /proc/meminfo 解析、磁盘与网络真实环境读取。
/// </summary>
public class ProcessResourceSourceTests
{
    [Fact]
    public void Read_全部读数委托抛错_按默认值处理不抛出()
    {
        var source = new ProcessResourceSource(
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException());
        var counters = source.Read();
        Assert.Equal(TimeSpan.Zero, counters.ProcessCpuTime);
        Assert.Equal(0, counters.ProcessMemoryMB);
        Assert.Equal(0, counters.SystemMemoryTotalMB);
        Assert.Equal(0, counters.DiskUsedGB);
        Assert.Equal(0, counters.NetRxBytes);
        Assert.Equal(0, counters.NetTxBytes);
    }

    [Fact]
    public void Read_默认构造_真实环境读数不抛出()
    {
        var source = new ProcessResourceSource();
        var counters = source.Read();
        Assert.True(counters.ProcessMemoryMB > 0);   // 当前进程工作集必然大于 0
        Assert.True(counters.ProcessorCount >= 1);
        Assert.True(counters.ProcessCpuTime >= TimeSpan.Zero);
    }

    [Fact]
    public void ReadSystemMemoryTotal_真实meminfo文件_解析MemTotal()
    {
        var path = Path.Combine(Path.GetTempPath(), "tao-meminfo-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllLines(path,
            [
                "MemTotal:       8388608 kB",
                "MemFree:         1024000 kB"
            ]);
            Assert.Equal(8192, ProcessResourceSource.ReadSystemMemoryTotal(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadSystemMemoryTotal_文件不存在_返回0()
    {
        Assert.Equal(0, ProcessResourceSource.ReadSystemMemoryTotal("/nonexistent/proc/meminfo"));
    }

    [Fact]
    public void ReadSystemMemoryTotal_MemTotal行缺失_返回0()
    {
        var path = Path.Combine(Path.GetTempPath(), "tao-meminfo-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllLines(path, ["MemFree:         1024000 kB"]);
            Assert.Equal(0, ProcessResourceSource.ReadSystemMemoryTotal(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadDisk_真实环境_总量与已用非负()
    {
        var (used, total) = ProcessResourceSource.ReadDisk();
        Assert.True(total >= 0);
        Assert.True(used >= 0);
        Assert.True(used <= total || total == 0);
    }

    [Fact]
    public void ReadNetwork_真实环境_不抛出且收发非负()
    {
        var (rx, tx) = ProcessResourceSource.ReadNetwork();
        Assert.True(rx >= 0);
        Assert.True(tx >= 0);
    }
}

/// <summary>资源采样历史环形缓冲：容量淘汰、最新查询、时间过滤、容量校验。</summary>
public class ResourceHistoryStoreTests
{
    private static ResourceSnapshot Snapshot(string service, DateTime at) => new()
    {
        ServiceName = service, CollectedAt = at, CpuPercent = at.Second
    };

    [Fact]
    public void Add_超容量_淘汰最旧()
    {
        var store = new ResourceHistoryStore(capacity: 3);
        for (var i = 0; i < 5; i++)
            store.Add(Snapshot("svc", new DateTime(2026, 1, 1, 0, 0, i)));
        var all = store.Since("svc", DateTime.MinValue);
        Assert.Equal(3, all.Count);
        Assert.Equal(2, all[0].CollectedAt.Second); // 最旧两条被淘汰
        Assert.Equal(4, all[^1].CollectedAt.Second);
    }

    [Fact]
    public void LatestOf_无采样_ReturnNull()
    {
        var store = new ResourceHistoryStore();
        Assert.Null(store.LatestOf("svc"));
    }

    [Fact]
    public void LatestOf_有采样_Return最新一条()
    {
        var store = new ResourceHistoryStore();
        store.Add(Snapshot("svc", new DateTime(2026, 1, 1, 0, 0, 1)));
        store.Add(Snapshot("svc", new DateTime(2026, 1, 1, 0, 0, 2)));
        Assert.Equal(2, store.LatestOf("svc")!.CollectedAt.Second);
    }

    [Fact]
    public void Since_按时间过滤_旧到新()
    {
        var store = new ResourceHistoryStore();
        var from = new DateTime(2026, 1, 1, 0, 0, 3);
        store.Add(Snapshot("svc", new DateTime(2026, 1, 1, 0, 0, 1)));
        store.Add(Snapshot("svc", from));
        store.Add(Snapshot("svc", new DateTime(2026, 1, 1, 0, 0, 5)));
        var since = store.Since("svc", from);
        Assert.Equal(2, since.Count);
        Assert.Equal(3, since[0].CollectedAt.Second);
        Assert.Equal(5, since[1].CollectedAt.Second);
    }

    [Fact]
    public void Since_未知服务_Return空列表()
    {
        var store = new ResourceHistoryStore();
        Assert.Empty(store.Since("nobody", DateTime.MinValue));
    }

    [Fact]
    public void LatestAll_每服务最新一条_无采样服务不在列()
    {
        var store = new ResourceHistoryStore();
        store.Add(Snapshot("a", new DateTime(2026, 1, 1, 0, 0, 1)));
        store.Add(Snapshot("a", new DateTime(2026, 1, 1, 0, 0, 2)));
        store.Add(Snapshot("b", new DateTime(2026, 1, 1, 0, 0, 3)));
        var latest = store.LatestAll();
        Assert.Equal(2, latest.Count);
        Assert.Contains(latest, s => s.ServiceName == "a" && s.CollectedAt.Second == 2);
        Assert.Contains(latest, s => s.ServiceName == "b" && s.CollectedAt.Second == 3);
        // 服务清单与已登记采样历史的服务一致
        Assert.Equal(2, store.Services.Count);
        Assert.Contains("a", store.Services);
        Assert.Contains("b", store.Services);
    }

    [Fact]
    public void 构造_容量小于1_抛ArgumentOutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceHistoryStore(capacity: 0));
    }
}

/// <summary>资源阈值评估（纯函数）：分级、四态迁移、事件结构。</summary>
public class ResourceThresholdEvaluatorTests
{
    private static ResourceSnapshot Snapshot(double cpu = 10, double memory = 20, double disk = 30) => new()
    {
        ServiceName = "taoadmin-test",
        CollectedAt = new DateTime(2026, 1, 1, 12, 0, 0),
        CpuPercent = cpu, MemoryPercent = memory, DiskPercent = disk
    };

    private static readonly ResourceThresholds Defaults = new(80, 85, 90);

    [Fact]
    public void Evaluate_全部正常_不超限()
    {
        var metrics = ResourceThresholdEvaluator.Evaluate(Snapshot(), Defaults);
        Assert.All(metrics, m => Assert.False(m.Over));
    }

    [Fact]
    public void Evaluate_超阈值10以内_Warning()
    {
        var metrics = ResourceThresholdEvaluator.Evaluate(Snapshot(cpu: 85), Defaults);
        var cpu = metrics.Single(m => m.Metric == "cpu");
        Assert.True(cpu.Over);
        Assert.Equal("Warning", cpu.Severity);
    }

    [Fact]
    public void Evaluate_超阈值10以上_Critical()
    {
        var metrics = ResourceThresholdEvaluator.Evaluate(Snapshot(cpu: 95), Defaults);
        var cpu = metrics.Single(m => m.Metric == "cpu");
        Assert.True(cpu.Over);
        Assert.Equal("Critical", cpu.Severity);
    }

    [Fact]
    public void EvaluateTransition_正常转超限_发告警事件()
    {
        var (evt, alerting) = ResourceThresholdEvaluator.EvaluateTransition(Snapshot(cpu: 95), Defaults, wasAlerting: false);
        Assert.True(alerting);
        Assert.NotNull(evt);
        Assert.Equal("resource.threshold", evt!.Type);
        Assert.Equal("Critical", evt.Severity);
        Assert.Equal("taoadmin-test", evt.Source);
        Assert.Contains("CPU", evt.Content);
        // 事件结构完整：事件 ID 与逐指标数据随事件携带
        Assert.False(string.IsNullOrEmpty(evt.EventId));
        Assert.NotNull(evt.Data);
        Assert.Equal("95.0", evt.Data!["cpu"]);
    }

    [Fact]
    public void EvaluateTransition_持续超限_不重复发事件()
    {
        var (_, alerting1) = ResourceThresholdEvaluator.EvaluateTransition(Snapshot(cpu: 95), Defaults, wasAlerting: false);
        var (evt2, alerting2) = ResourceThresholdEvaluator.EvaluateTransition(Snapshot(cpu: 96), Defaults, wasAlerting: alerting1);
        Assert.True(alerting2);
        Assert.Null(evt2);
    }

    [Fact]
    public void EvaluateTransition_超限转恢复_发恢复事件()
    {
        var (evt, alerting) = ResourceThresholdEvaluator.EvaluateTransition(Snapshot(), Defaults, wasAlerting: true);
        Assert.False(alerting);
        Assert.NotNull(evt);
        Assert.Equal("resource.recovered", evt!.Type);
        Assert.Equal("Info", evt.Severity);
        Assert.Contains("恢复", evt.Title);
    }

    [Fact]
    public void EvaluateTransition_持续正常_ReturnNull()
    {
        var (evt, alerting) = ResourceThresholdEvaluator.EvaluateTransition(Snapshot(), Defaults, wasAlerting: false);
        Assert.False(alerting);
        Assert.Null(evt);
    }

    [Fact]
    public void EvaluateTransition_多指标超限_取最高级别()
    {
        // CPU 95（Critical）+ 内存 88（Warning）→ 事件级别取 Critical
        var (evt, _) = ResourceThresholdEvaluator.EvaluateTransition(Snapshot(cpu: 95, memory: 88), Defaults, wasAlerting: false);
        Assert.NotNull(evt);
        Assert.Equal("Critical", evt!.Severity);
        Assert.Contains("CPU", evt.Content);
        Assert.Contains("内存", evt.Content);
    }

    [Fact]
    public void MetricLabel_已知指标_映射中文标签()
    {
        Assert.Equal("CPU", ResourceThresholdEvaluator.MetricLabel("cpu"));
        Assert.Equal("内存", ResourceThresholdEvaluator.MetricLabel("memory"));
        Assert.Equal("磁盘", ResourceThresholdEvaluator.MetricLabel("disk"));
    }

    [Fact]
    public void MetricLabel_未知指标_原样返回()
    {
        Assert.Equal("gpu", ResourceThresholdEvaluator.MetricLabel("gpu"));
    }
}

/// <summary>内存告警事件发布器：缓冲容量、类型过滤、sink 异常不反噬。</summary>
public class InMemoryAlertEventPublisherTests
{
    private static AlertEvent Event(string type = "resource.threshold", string severity = "Warning") => new(
        EventId: Guid.NewGuid().ToString("N"), Type: type, Severity: severity,
        Source: "svc", Title: "标题", Content: "内容", OccurredAt: DateTime.Now);

    [Fact]
    public async Task Publish_入缓冲_Recent可查()
    {
        var publisher = new InMemoryAlertEventPublisher();
        await publisher.PublishAsync(Event());
        Assert.Single(publisher.Recent(10));
    }

    [Fact]
    public async Task Publish_超容量_淘汰最旧()
    {
        var publisher = new InMemoryAlertEventPublisher(capacity: 3);
        for (var i = 0; i < 5; i++)
            await publisher.PublishAsync(Event());
        var recent = publisher.Recent(10);
        Assert.Equal(3, recent.Count);
    }

    [Fact]
    public async Task Recent_按类型过滤()
    {
        var publisher = new InMemoryAlertEventPublisher();
        await publisher.PublishAsync(Event(type: "resource.threshold"));
        await publisher.PublishAsync(Event(type: "resource.recovered"));
        Assert.Single(publisher.Recent(10, "resource.threshold"));
        Assert.Single(publisher.Recent(10, "resource.recovered"));
        Assert.Equal(2, publisher.Recent(10).Count);
    }

    [Fact]
    public async Task Recent_按数量截取_新到旧()
    {
        var publisher = new InMemoryAlertEventPublisher();
        for (var i = 0; i < 5; i++)
            await publisher.PublishAsync(Event());
        Assert.Equal(2, publisher.Recent(2).Count);
    }

    [Fact]
    public async Task Publish_Sink抛错_不反噬业务()
    {
        var publisher = new InMemoryAlertEventPublisher(sink: _ => throw new InvalidOperationException());
        await publisher.PublishAsync(Event());
        // sink 抛错不影响缓冲照常留存
        Assert.Single(publisher.Recent(10));
    }
}
