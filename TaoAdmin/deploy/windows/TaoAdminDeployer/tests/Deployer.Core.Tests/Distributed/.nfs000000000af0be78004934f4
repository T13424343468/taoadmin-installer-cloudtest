using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class PlacementPlannerTests
{
    private static ClusterTopology Topo(int nodes, params ServiceRole[] roles)
    {
        var topo = new ClusterTopology();
        for (var i = 1; i <= nodes; i++)
            topo.Nodes.Add(new ClusterNode { Id = $"n{i}", Name = $"机器{i}", Os = NodeOs.Linux, Host = $"10.0.0.{i}" });
        topo.Roles.AddRange(roles);
        return topo;
    }

    [Fact]
    public void Recommend_SingleNode_AllOnOneMachine()
    {
        var topo = Topo(1,
            new ServiceRole { Id = "db", Name = "MySQL", Kind = ServiceKind.Database },
            new ServiceRole { Id = "gw", Name = "网关", Kind = ServiceKind.Gateway },
            new ServiceRole { Id = "app", Name = "权限服务", Kind = ServiceKind.AppService, Instances = 2 });
        var plan = PlacementPlanner.Recommend(topo);
        Assert.All(plan, p => Assert.Equal("n1", p.NodeId));
        Assert.Equal(4, plan.Count); // db 1 + gw 1 + app 2
    }

    [Fact]
    public void Recommend_MultiNode_MiddlewareFirst_EdgeSecond_AppsSpread()
    {
        var topo = Topo(3,
            new ServiceRole { Id = "db", Name = "MySQL", Kind = ServiceKind.Database },
            new ServiceRole { Id = "gw", Name = "网关", Kind = ServiceKind.Gateway },
            new ServiceRole { Id = "app", Name = "权限服务", Kind = ServiceKind.AppService, Instances = 3 });
        var plan = PlacementPlanner.Recommend(topo);
        Assert.All(plan.Where(p => p.RoleId == "db"), p => Assert.Equal("n1", p.NodeId));
        Assert.All(plan.Where(p => p.RoleId == "gw"), p => Assert.Equal("n2", p.NodeId));
        var appNodes = plan.Where(p => p.RoleId == "app").Select(p => p.NodeId).ToHashSet();
        Assert.Equal(new HashSet<string> { "n3", "n1", "n2" }, appNodes); // 从 n3 起轮转，3 实例铺满 3 台
    }

    [Fact]
    public void Recommend_EmptyNodes_Throws()
    {
        var topo = new ClusterTopology { Roles = { new ServiceRole { Id = "db" } } };
        var ex = Assert.Throws<InvalidOperationException>(() => PlacementPlanner.Recommend(topo));
        Assert.Contains("机器清单为空", ex.Message);
    }

    [Fact]
    public void Recommend_EmptyRoles_Throws()
    {
        var topo = Topo(1);
        var ex = Assert.Throws<InvalidOperationException>(() => PlacementPlanner.Recommend(topo));
        Assert.Contains("服务角色为空", ex.Message);
    }

    [Fact]
    public void MoveRole_MovesAllInstances_AndValidatesTarget()
    {
        var topo = Topo(2, new ServiceRole { Id = "app", Instances = 2 });
        topo.Placements.AddRange(PlacementPlanner.Recommend(topo)); // 2 实例轮转落 n1/n2
        var moved = PlacementPlanner.MoveRole(topo, "app", "n2"); // 只挪不在 n2 的那台
        Assert.Equal(1, moved);
        Assert.All(topo.Placements.Where(p => p.RoleId == "app"), p => Assert.Equal("n2", p.NodeId));

        var ex = Assert.Throws<InvalidOperationException>(() => PlacementPlanner.MoveRole(topo, "app", "nope"));
        Assert.Contains("目标机器不存在", ex.Message);
        Assert.Throws<ArgumentException>(() => PlacementPlanner.MoveRole(topo, "", "n1"));
    }

    [Fact]
    public void RebalanceRole_SpreadsAcrossAllNodes_AndReplacesOld()
    {
        var topo = Topo(2, new ServiceRole { Id = "app", Instances = 2 });
        topo.Placements.AddRange(PlacementPlanner.Recommend(topo)); // 全在 n1
        topo.Nodes.Add(new ClusterNode { Id = "n3", Host = "10.0.0.3" }); // 加机器
        var rebalanced = PlacementPlanner.RebalanceRole(topo, "app");
        Assert.Equal(2, rebalanced.Count);
        Assert.Equal(new HashSet<string> { "n1", "n2" }, rebalanced.Select(p => p.NodeId).ToHashSet());
        Assert.Equal(2, topo.Placements.Count(p => p.RoleId == "app")); // 旧分配被替换

        var ex = Assert.Throws<InvalidOperationException>(() => PlacementPlanner.RebalanceRole(topo, "ghost"));
        Assert.Contains("角色不存在", ex.Message);
    }
}
