// ⑳分布式安装器：集群模型。中控+代理模式，机器×服务实例矩阵分配。
namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>被控机环境类型。</summary>
public enum NodeOs
{
    Linux,
    Windows,
}

/// <summary>被控机接入方式。</summary>
public enum NodeAccess
{
    /// <summary>SSH 引导（Linux 全支持；Windows 需系统可选 SSH 已开启）。</summary>
    Ssh,

    /// <summary>手动安装代理包（Windows 无 SSH 时的兜底）。</summary>
    AgentPackage,
}

/// <summary>被控机节点。</summary>
public sealed class ClusterNode
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public NodeOs Os { get; set; } = NodeOs.Linux;
    public string Host { get; set; } = "";
    public int SshPort { get; set; } = 22;
    public NodeAccess Access { get; set; } = NodeAccess.Ssh;

    /// <summary>SSH 登录名。密码/密钥不落本类，凭据由 CredentialVault 单独加密托管。</summary>
    public string SshUser { get; set; } = "root";

    /// <summary>代理通道端口（首次 SSH 引导后代理监听）。</summary>
    public int AgentPort { get; set; } = 5400;

    public NodeState State { get; set; } = NodeState.Pending;
    public string? LastError { get; set; }
}

/// <summary>节点状态机：待接入→引导中→在线→离线/失败。</summary>
public enum NodeState
{
    Pending,
    Bootstrapping,
    Online,
    Offline,
    Failed,
}

/// <summary>服务角色：可分配到节点上的最小单位（一个服务或中间件）。</summary>
public sealed class ServiceRole
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ServiceKind Kind { get; set; } = ServiceKind.AppService;

    /// <summary>实例数（同一角色可多实例分摊到多机互为备份）。</summary>
    public int Instances { get; set; } = 1;

    /// <summary>是否中间件（决定推荐布局分组）。</summary>
    public bool IsMiddleware => Kind is ServiceKind.Database or ServiceKind.Cache or ServiceKind.Queue or ServiceKind.Search;
}

public enum ServiceKind
{
    Gateway,
    AppService,
    Frontend,
    Database,
    Cache,
    Queue,
    Search,
}

/// <summary>分配项：哪个角色第几个实例落在哪台机器。</summary>
public sealed class Placement
{
    public string RoleId { get; set; } = "";
    public int InstanceIndex { get; set; }
    public string NodeId { get; set; } = "";
}

/// <summary>集群拓扑：机器清单 + 角色清单 + 分配矩阵。</summary>
public sealed class ClusterTopology
{
    public List<ClusterNode> Nodes { get; set; } = new();
    public List<ServiceRole> Roles { get; set; } = new();
    public List<Placement> Placements { get; set; } = new();

    /// <summary>中间件高可用可选项（主从/哨兵），不强制。</summary>
    public bool HaEnabled { get; set; }
}
