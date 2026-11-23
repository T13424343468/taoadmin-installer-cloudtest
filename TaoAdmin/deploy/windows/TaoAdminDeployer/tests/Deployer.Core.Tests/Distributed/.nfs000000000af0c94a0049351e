using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class ClusterOrchestratorTests
{
    private sealed class FakeExecutor : INodeExecutor
    {
        public HashSet<string> FailNodes { get; set; } = new();
        public List<string> Executed { get; } = new();
        public int ConcurrencySeen { get; private set; }
        private int _running;

        public Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct)
        {
            Executed.Add(node.Id);
            Interlocked.Increment(ref _running);
            ConcurrencySeen = Math.Max(ConcurrencySeen, _running);
            try
            {
                ledger.Update(new AgentProgress { NodeId = node.Id, Phase = "pull", Percent = 50 });
                var fail = FailNodes.Contains(node.Id);
                return Task.FromResult(new AgentTaskResult
                {
                    TaskId = task.TaskId,
                    Success = !fail,
                    Error = fail ? "模拟失败" : null,
                });
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }

    private static ClusterTopology Topo(params string[] ids)
    {
        var topo = new ClusterTopology();
        foreach (var id in ids)
            topo.Nodes.Add(new ClusterNode { Id = id, Host = "10.0.0.x", State = NodeState.Online });
        return topo;
    }

    [Fact]
    public async Task InstallAll_RunsAllNodes_ReportsProgress()
    {
        var exec = new FakeExecutor();
        var orch = new ClusterOrchestrator(exec);
        var topo = Topo("n1", "n2", "n3");

        var results = await orch.InstallAllAsync(topo, "t1", failFast: false, CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.All(topo.Nodes, n => Assert.Equal(50, orch.Ledger.Get(n.Id)!.Percent));
    }

    [Fact]
    public async Task InstallAll_FailFast_StopsOnFirstFailure()
    {
        var exec = new FakeExecutor { FailNodes = { "n2" } };
        var orch = new ClusterOrchestrator(exec);
        var topo = Topo("n1", "n2", "n3");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.InstallAllAsync(topo, "t1", failFast: true, CancellationToken.None));
        Assert.Contains("滚动停住", ex.Message);
        Assert.Contains("n2", ex.Message);
    }

    [Fact]
    public async Task InstallAll_NoOnlineNodes_Throws()
    {
        var orch = new ClusterOrchestrator(new FakeExecutor());
        var topo = Topo("n1");
        topo.Nodes[0].State = NodeState.Offline;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.InstallAllAsync(topo, "t1", false, CancellationToken.None));
        Assert.Contains("没有在线机器", ex.Message);
    }

    [Fact]
    public async Task RollingUpgrade_StopsAtBadNode_DoesNotContinue()
    {
        var exec = new FakeExecutor { FailNodes = { "n2" } };
        var orch = new ClusterOrchestrator(exec);
        var topo = Topo("n1", "n2", "n3");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.RollingUpgradeAsync(topo, new[] { "n1", "n2", "n3" }, "u1", CancellationToken.None));
        Assert.Contains("n2", ex.Message);
        Assert.DoesNotContain("n3", exec.Executed); // 坏机后不再往下走
    }

    [Fact]
    public async Task RollingUpgrade_OfflineNode_Stops()
    {
        var exec = new FakeExecutor();
        var orch = new ClusterOrchestrator(exec);
        var topo = Topo("n1", "n2");
        topo.Nodes[1].State = NodeState.Offline;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.RollingUpgradeAsync(topo, new[] { "n1", "n2" }, "u1", CancellationToken.None));
        Assert.Contains("不在线", ex.Message);
    }

    [Fact]
    public async Task RollingUpgrade_AllGood_ExecutesInOrder()
    {
        var exec = new FakeExecutor();
        var orch = new ClusterOrchestrator(exec);
        var topo = Topo("n1", "n2", "n3");

        var results = await orch.RollingUpgradeAsync(topo, new[] { "n3", "n1", "n2" }, "u1", CancellationToken.None);
        Assert.Equal(new[] { "n3", "n1", "n2" }, exec.Executed);
        Assert.Equal(3, results.Count);
    }
}
