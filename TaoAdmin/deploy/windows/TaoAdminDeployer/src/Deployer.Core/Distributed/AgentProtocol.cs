// ⑳分布式安装器：中控-代理通信协议。代理上报进度/状态，中控下发任务。
// 传输：HTTP JSON（代理监听 Node.AgentPort，中控轮询+下发）。
namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>代理上报的机器执行进度。</summary>
public sealed class AgentProgress
{
    public string NodeId { get; set; } = "";
    public string Phase { get; set; } = "";   // env | pull | start | health
    public int Percent { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>中控下发给某台机器的任务。</summary>
public sealed class AgentTask
{
    public string TaskId { get; set; } = "";
    public string Kind { get; set; } = "";    // bootstrap | install | start | stop | health | upgrade
    public string Payload { get; set; } = ""; // JSON
}

/// <summary>任务执行结果。</summary>
public sealed class AgentTaskResult
{
    public string TaskId { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> Output { get; set; } = new();
}

/// <summary>中控侧对单机的进度账本（线程安全，界面轮询读）。</summary>
public sealed class NodeProgressLedger
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AgentProgress> _latest = new();

    public void Update(AgentProgress p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (string.IsNullOrWhiteSpace(p.NodeId)) throw new ArgumentException("进度缺 NodeId", nameof(p));
        if (p.Percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(p), "进度须在 0-100");
        lock (_lock) { _latest[p.NodeId] = p; }
    }

    public AgentProgress? Get(string nodeId)
    {
        lock (_lock) { return _latest.TryGetValue(nodeId, out var v) ? v : null; }
    }

    public IReadOnlyList<AgentProgress> Snapshot()
    {
        lock (_lock) { return _latest.Values.ToList(); }
    }
}

/// <summary>滚动更新编排：逐台更新，坏一台自动停住不往下走。</summary>
public static class RollingUpgrader
{
    /// <summary>
    /// 计算滚动更新批次：按分配矩阵分组，同批机器并行、批间串行。
    /// 任一批失败即停，后续批次不下发。
    /// </summary>
    public static List<List<string>> PlanBatches(ClusterTopology topo, IReadOnlyList<string> order)
    {
        ArgumentNullException.ThrowIfNull(topo);
        ArgumentNullException.ThrowIfNull(order);
        if (order.Count == 0) throw new InvalidOperationException("滚动更新顺序为空");
        var known = topo.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var id in order)
            if (!known.Contains(id)) throw new InvalidOperationException($"滚动更新顺序含未知机器：{id}");
        // 每台机器独立一批（滚动口径：逐台更新，坏一台停住）
        return order.Select(id => new List<string> { id }).ToList();
    }
}
