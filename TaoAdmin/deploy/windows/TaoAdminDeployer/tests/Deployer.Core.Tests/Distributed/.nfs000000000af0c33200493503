using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class AgentProtocolTests
{
    [Fact]
    public void Ledger_UpdateAndGet()
    {
        var ledger = new NodeProgressLedger();
        ledger.Update(new AgentProgress { NodeId = "n1", Phase = "pull", Percent = 40, Message = "拉镜像中" });
        var got = ledger.Get("n1");
        Assert.NotNull(got);
        Assert.Equal(40, got!.Percent);
        Assert.Equal("pull", got.Phase);
    }

    [Fact]
    public void Ledger_LatestWins()
    {
        var ledger = new NodeProgressLedger();
        ledger.Update(new AgentProgress { NodeId = "n1", Percent = 10 });
        ledger.Update(new AgentProgress { NodeId = "n1", Percent = 90 });
        Assert.Equal(90, ledger.Get("n1")!.Percent);
        Assert.Single(ledger.Snapshot());
    }

    [Fact]
    public void Ledger_RejectsBadInput()
    {
        var ledger = new NodeProgressLedger();
        Assert.Throws<ArgumentException>(() => ledger.Update(new AgentProgress { NodeId = "", Percent = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Update(new AgentProgress { NodeId = "n1", Percent = 101 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Update(new AgentProgress { NodeId = "n1", Percent = -1 }));
        Assert.Null(ledger.Get("ghost"));
    }

    [Fact]
    public void RollingBatches_EachNodeOwnBatch_UnknownThrows()
    {
        var topo = new ClusterTopology
        {
            Nodes = { new ClusterNode { Id = "n1" }, new ClusterNode { Id = "n2" } },
        };
        var batches = RollingUpgrader.PlanBatches(topo, new[] { "n1", "n2" });
        Assert.Equal(2, batches.Count);
        Assert.Single(batches[0]);
        Assert.Equal("n1", batches[0][0]);

        var ex = Assert.Throws<InvalidOperationException>(() => RollingUpgrader.PlanBatches(topo, new[] { "n1", "ghost" }));
        Assert.Contains("未知机器", ex.Message);
        Assert.Throws<InvalidOperationException>(() => RollingUpgrader.PlanBatches(topo, Array.Empty<string>()));
    }
}
