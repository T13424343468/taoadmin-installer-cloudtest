using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

/// <summary>
/// 升级/回滚全链路实效断言：升级成功记账、回滚按旧清单逐台下装、
/// 无账停住、坏机停住不盲滚、离线机器拒绝回滚。
/// </summary>
public class UpgradeRollbackTests
{
    private sealed class RecordingExecutor : INodeExecutor
    {
        public List<(ClusterNode Node, AgentTask Task)> Executed { get; } = new();
        public Func<ClusterNode, AgentTask, AgentTaskResult> Responder { get; set; } =
            (n, t) => new AgentTaskResult { TaskId = t.TaskId, Success = true };

        public Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct)
        {
            Executed.Add((node, task));
            return Task.FromResult(Responder(node, task));
        }
    }

    private static ClusterTopology Topo() => new()
    {
        Nodes = new List<ClusterNode>
        {
            new() { Id = "n1", Name = "一", Host = "10.0.0.1", State = NodeState.Online },
            new() { Id = "n2", Name = "二", Host = "10.0.0.2", State = NodeState.Online },
        },
        Roles = new List<ServiceRole> { new() { Id = "app", Name = "应用" } },
    };

    [Fact]
    public async Task Rollback_ShouldReinstallLastGoodManifest_InOrder()
    {
        var ledger = new UpgradeLedger();
        ledger.RecordGood("n1", "{\"good\":\"v1-n1\"}");
        ledger.RecordGood("n2", "{\"good\":\"v1-n2\"}");

        var executor = new RecordingExecutor();
        var results = await ClusterRollback.RollbackAsync(executor, Topo(), ledger,
            new List<string> { "n1", "n2" }, "rb", new NodeProgressLedger(), CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.True(results.All(r => r.Success));
        // 逐台按序下发 install 任务，载荷=各自的上次可用清单
        Assert.Equal("install", executor.Executed[0].Task.Kind);
        Assert.Equal("{\"good\":\"v1-n1\"}", executor.Executed[0].Task.Payload);
        Assert.Equal("n1", executor.Executed[0].Node.Id);
        Assert.Equal("{\"good\":\"v1-n2\"}", executor.Executed[1].Task.Payload);
        Assert.Equal("n2", executor.Executed[1].Node.Id);
    }

    [Fact]
    public async Task Rollback_NodeWithoutHistory_ShouldStopBeforeIt()
    {
        var ledger = new UpgradeLedger();
        ledger.RecordGood("n1", "{\"good\":\"v1\"}");
        // n2 从未成功安装过：无账可查

        var executor = new RecordingExecutor();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ClusterRollback.RollbackAsync(executor, Topo(), ledger,
                new List<string> { "n1", "n2" }, "rb", new NodeProgressLedger(), CancellationToken.None));

        Assert.Contains("n2", ex.Message);
        Assert.Contains("无可用历史清单", ex.Message);
        // 停住：n1 已滚，n2 未执行（异常先于执行抛出）
        Assert.Single(executor.Executed);
    }

    [Fact]
    public async Task Rollback_FailedNode_ShouldStopRemainder()
    {
        var ledger = new UpgradeLedger();
        ledger.RecordGood("n1", "{\"good\":\"v1\"}");
        ledger.RecordGood("n2", "{\"good\":\"v1\"}");

        var executor = new RecordingExecutor
        {
            Responder = (n, _) => n.Id == "n1"
                ? new AgentTaskResult { TaskId = "rb:n1", Success = false, Error = "旧镜像拉取失败" }
                : new AgentTaskResult { TaskId = "rb:n2", Success = true },
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ClusterRollback.RollbackAsync(executor, Topo(), ledger,
                new List<string> { "n1", "n2" }, "rb", new NodeProgressLedger(), CancellationToken.None));

        Assert.Contains("n1", ex.Message);
        Assert.Contains("回滚失败", ex.Message);
        Assert.Single(executor.Executed); // n1 失败即停，n2 不动
    }

    [Fact]
    public async Task Rollback_OfflineNode_ShouldRefuse()
    {
        var ledger = new UpgradeLedger();
        ledger.RecordGood("n1", "{\"good\":\"v1\"}");
        var topo = Topo();
        topo.Nodes[0].State = NodeState.Offline;

        var executor = new RecordingExecutor();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ClusterRollback.RollbackAsync(executor, topo, ledger,
                new List<string> { "n1" }, "rb", new NodeProgressLedger(), CancellationToken.None));

        Assert.Contains("不在线", ex.Message);
        Assert.Empty(executor.Executed);
    }

    [Fact]
    public void Ledger_RecordGood_ShouldKeepLatestOnly()
    {
        var ledger = new UpgradeLedger();
        ledger.RecordGood("n1", "{\"v\":1}");
        ledger.RecordGood("n1", "{\"v\":2}");

        Assert.Equal("{\"v\":2}", ledger.LastGood("n1"));
        Assert.True(ledger.CanRollback("n1"));
        Assert.False(ledger.CanRollback("ghost"));
    }

    [Fact]
    public void Ledger_BlankManifest_ShouldThrow()
    {
        var ledger = new UpgradeLedger();
        Assert.Throws<ArgumentException>(() => ledger.RecordGood("n1", "  "));
    }
}
