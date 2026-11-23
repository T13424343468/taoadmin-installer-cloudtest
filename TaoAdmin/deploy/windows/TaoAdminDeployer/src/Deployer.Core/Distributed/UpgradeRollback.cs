// ⑳分布式安装器：升级/回滚全链路。
// UpgradeLedger：每台机器的"上次可用清单"留痕（升级前先记账，回滚有据可依）。
// ClusterRollback：逐台回滚——把上次可用清单重新下发安装；无账可查即停住，不盲滚。
namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>升级账本：记录每台机器最近一次可用部署清单（JSON），回滚时取用。</summary>
public sealed class UpgradeLedger
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _lastGood = new();

    /// <summary>升级成功后记账（覆盖旧账，只留最近一次可用）。</summary>
    public void RecordGood(string nodeId, string manifestJson)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("节点不能为空", nameof(nodeId));
        if (string.IsNullOrWhiteSpace(manifestJson)) throw new ArgumentException("清单不能为空", nameof(manifestJson));
        lock (_lock) { _lastGood[nodeId] = manifestJson; }
    }

    /// <summary>取某机最近可用清单；无账返回 null（该机从未成功装过，回滚无从谈起）。</summary>
    public string? LastGood(string nodeId)
    {
        lock (_lock) { return _lastGood.TryGetValue(nodeId, out var v) ? v : null; }
    }

    /// <summary>是否有某机的回滚依据。</summary>
    public bool CanRollback(string nodeId)
    {
        lock (_lock) { return _lastGood.ContainsKey(nodeId); }
    }
}

/// <summary>
/// 回滚编排：逐台把上次可用清单重新下发（install 任务，Payload=旧清单）。
/// 任一台无账可查或回滚失败即停住，后续机器不动——与滚动升级同一停住口径。
/// </summary>
public static class ClusterRollback
{
    public static async Task<IReadOnlyList<AgentTaskResult>> RollbackAsync(
        INodeExecutor executor,
        ClusterTopology topo,
        UpgradeLedger ledger,
        IReadOnlyList<string> order,
        string taskId,
        NodeProgressLedger progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(topo);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(progress);
        if (order.Count == 0) throw new InvalidOperationException("回滚顺序为空");

        var results = new List<AgentTaskResult>();
        foreach (var nodeId in order)
        {
            var node = topo.Nodes.FirstOrDefault(n => n.Id == nodeId)
                ?? throw new InvalidOperationException($"回滚顺序含未知机器：{nodeId}");
            if (node.State != NodeState.Online)
                throw new InvalidOperationException($"回滚停住：机器 {nodeId} 不在线（{node.State}）");

            var manifest = ledger.LastGood(nodeId)
                ?? throw new InvalidOperationException($"回滚停住：机器 {nodeId} 无可用历史清单（从未成功安装过，不能盲滚）");

            var result = await executor.ExecuteAsync(node,
                new AgentTask { TaskId = $"{taskId}:{nodeId}", Kind = "install", Payload = manifest }, progress, ct);
            results.Add(result);

            if (!result.Success)
                throw new InvalidOperationException($"回滚停住：机器 {nodeId} 回滚失败——{result.Error}");
        }
        return results;
    }
}
