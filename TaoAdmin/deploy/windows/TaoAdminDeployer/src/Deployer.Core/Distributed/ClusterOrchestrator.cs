// ⑳分布式安装器：中控编排。并行下发、逐机进度账本、坏机停住。
namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>单机安装执行器抽象：中控把任务交给各机的执行通道（代理）。</summary>
public interface INodeExecutor
{
    /// <summary>在某台机器上执行安装阶段，进度经 ledger 上报。</summary>
    Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct);
}

public sealed class ClusterOrchestrator
{
    private readonly INodeExecutor _executor;
    private readonly NodeProgressLedger _ledger;

    public ClusterOrchestrator(INodeExecutor executor, NodeProgressLedger? ledger = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _ledger = ledger ?? new NodeProgressLedger();
    }

    public NodeProgressLedger Ledger => _ledger;

    /// <summary>
    /// 并行安装：所有在线机器同时下发，每台机器进度实时入账。
    /// failFast=true 时任一台失败即取消其余（坏一台停住）；默认等全部出结果再汇总。
    /// </summary>
    public async Task<IReadOnlyList<AgentTaskResult>> InstallAllAsync(ClusterTopology topo, string taskId, bool failFast, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(topo);
        var online = topo.Nodes.Where(n => n.State == NodeState.Online).ToList();
        if (online.Count == 0) throw new InvalidOperationException("没有在线机器可下发");

        var tasks = online.Select(n => Task.Run(async () =>
        {
            var t = new AgentTask { TaskId = $"{taskId}:{n.Id}", Kind = "install" };
            return await _executor.ExecuteAsync(n, t, _ledger, ct);
        }, ct)).ToList();

        var results = new List<AgentTaskResult>();
        var remaining = tasks.ToList();
        while (remaining.Count > 0)
        {
            var done = await Task.WhenAny(remaining);
            remaining.Remove(done);
            var r = await done;
            results.Add(r);
            if (failFast && !r.Success)
            {
                foreach (var t in remaining) try { t.Wait(ct); } catch (OperationCanceledException) { }
                throw new InvalidOperationException($"滚动停住：{r.TaskId} 失败——{r.Error}");
            }
        }
        return results;
    }

    /// <summary>滚动升级：逐台更新，坏一台自动停住不往下走。</summary>
    public async Task<IReadOnlyList<AgentTaskResult>> RollingUpgradeAsync(ClusterTopology topo, IReadOnlyList<string> order, string taskId, CancellationToken ct)
    {
        var batches = RollingUpgrader.PlanBatches(topo, order);
        var results = new List<AgentTaskResult>();
        foreach (var batch in batches)
        {
            foreach (var nodeId in batch)
            {
                var node = topo.Nodes.First(n => n.Id == nodeId);
                if (node.State != NodeState.Online)
                    throw new InvalidOperationException($"滚动停住：机器 {nodeId} 不在线（{node.State}）");
                var r = await _executor.ExecuteAsync(node, new AgentTask { TaskId = $"{taskId}:{nodeId}", Kind = "upgrade" }, _ledger, ct);
                results.Add(r);
                if (!r.Success)
                    throw new InvalidOperationException($"滚动停住：机器 {nodeId} 升级失败——{r.Error}");
            }
        }
        return results;
    }
}
