// ⑳分布式安装器：推荐布局。从粗到细：中间件一组、网关+前端一组、应用服务按实例均摊。
namespace TaoAdmin.Deployer.Core.Distributed;

public static class PlacementPlanner
{
    /// <summary>
    /// 生成推荐布局：中间件优先落前面的机器（数据盘通常在独立机），
    /// 网关+前端落一台面向用户的机器，应用服务实例轮转均摊到剩余机器。
    /// 单机场景（nodes=1）全部落同一台，即分布式版的「一台机器」特例。
    /// </summary>
    public static List<Placement> Recommend(ClusterTopology topo)
    {
        ArgumentNullException.ThrowIfNull(topo);
        if (topo.Nodes.Count == 0)
            throw new InvalidOperationException("机器清单为空，无法生成布局");
        if (topo.Roles.Count == 0)
            throw new InvalidOperationException("服务角色为空，无法生成布局");

        var nodeIds = topo.Nodes.Select(n => n.Id).ToList();
        var result = new List<Placement>();

        // 中间件集中到第一台（推荐独立数据机）
        var midIdx = 0;
        foreach (var role in topo.Roles.Where(r => r.IsMiddleware))
        {
            for (var i = 0; i < Math.Max(1, role.Instances); i++)
                result.Add(new Placement { RoleId = role.Id, InstanceIndex = i, NodeId = nodeIds[midIdx % nodeIds.Count] });
            midIdx++;
        }

        // 网关+前端落第二台（面向用户的机器）；只有一台时同机
        var edgeIdx = nodeIds.Count > 1 ? 1 : 0;
        foreach (var role in topo.Roles.Where(r => r.Kind is ServiceKind.Gateway or ServiceKind.Frontend))
        {
            for (var i = 0; i < Math.Max(1, role.Instances); i++)
                result.Add(new Placement { RoleId = role.Id, InstanceIndex = i, NodeId = nodeIds[edgeIdx] });
        }

        // 应用服务实例轮转均摊到剩余机器；不足时从头轮转
        var appRoles = topo.Roles.Where(r => r.Kind == ServiceKind.AppService).ToList();
        var cursor = nodeIds.Count > 2 ? 2 : 0;
        foreach (var role in appRoles)
        {
            for (var i = 0; i < Math.Max(1, role.Instances); i++)
            {
                result.Add(new Placement { RoleId = role.Id, InstanceIndex = i, NodeId = nodeIds[cursor % nodeIds.Count] });
                cursor++;
            }
        }

        return result;
    }

    /// <summary>把某角色的全部实例从 A 机挪到 B 机（日常运维：挪服务）。</summary>
    public static int MoveRole(ClusterTopology topo, string roleId, string toNodeId)
    {
        ArgumentNullException.ThrowIfNull(topo);
        if (string.IsNullOrWhiteSpace(roleId)) throw new ArgumentException("角色不能为空", nameof(roleId));
        if (string.IsNullOrWhiteSpace(toNodeId)) throw new ArgumentException("目标机器不能为空", nameof(toNodeId));
        if (!topo.Nodes.Any(n => n.Id == toNodeId))
            throw new InvalidOperationException($"目标机器不存在：{toNodeId}");
        var moved = 0;
        foreach (var p in topo.Placements.Where(p => p.RoleId == roleId))
        {
            if (p.NodeId != toNodeId) { p.NodeId = toNodeId; moved++; }
        }
        return moved;
    }

    /// <summary>加机器扩容：新机器入列后，把指定角色的多余实例重新均摊。</summary>
    public static List<Placement> RebalanceRole(ClusterTopology topo, string roleId)
    {
        ArgumentNullException.ThrowIfNull(topo);
        var role = topo.Roles.FirstOrDefault(r => r.Id == roleId)
            ?? throw new InvalidOperationException($"角色不存在：{roleId}");
        var nodeIds = topo.Nodes.Select(n => n.Id).ToList();
        if (nodeIds.Count == 0) throw new InvalidOperationException("机器清单为空");

        var result = new List<Placement>();
        for (var i = 0; i < Math.Max(1, role.Instances); i++)
            result.Add(new Placement { RoleId = roleId, InstanceIndex = i, NodeId = nodeIds[i % nodeIds.Count] });

        topo.Placements.RemoveAll(p => p.RoleId == roleId);
        topo.Placements.AddRange(result);
        return result;
    }
}
