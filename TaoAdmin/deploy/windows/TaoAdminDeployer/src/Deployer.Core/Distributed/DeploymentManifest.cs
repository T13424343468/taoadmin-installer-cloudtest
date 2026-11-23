// ⑳分布式安装器：部署清单。集群拓扑 → 每机服务清单 → docker-compose.yml（分布式版）。
// 跨机网络口径：统一 compose 网络 taoadmin；跨机访问经 host-gateway 发布端口，
// 中间件地址以环境变量注入（TAO_DB_HOST / TAO_CACHE_HOST / TAO_QUEUE_HOST）。
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>角色部署规格：镜像、容器端口、健康路径、环境与卷。</summary>
public sealed record ServiceSpec(
    string Image,
    int ContainerPort,
    string? HealthPath = null,
    IReadOnlyList<string>? Env = null,
    IReadOnlyList<string>? Volumes = null);

/// <summary>单机服务清单项（compose services 下的一个服务）。</summary>
public sealed class ManifestService
{
    public string RoleId { get; set; } = "";
    public string Name { get; set; } = "";          // compose 服务名：{roleId}-{instanceIndex}
    public string Image { get; set; } = "";
    public int? HostPort { get; set; }              // 本机发布端口（null=仅集群内访问）
    public int ContainerPort { get; set; } = 8080;
    public List<string> Env { get; set; } = new();  // KEY=VALUE
    public List<string> Volumes { get; set; } = new(); // host:container
    public string? HealthPath { get; set; }
    public bool RestartAlways { get; set; } = true;

    [JsonIgnore]
    public string HealthcheckTest => HealthPath is null ? "" : $"curl -fsS http://127.0.0.1:{ContainerPort}{HealthPath}";
}

/// <summary>单机部署清单：一台机器上要跑的全部服务。</summary>
public sealed class NodeManifest
{
    public string NodeId { get; set; } = "";
    public string NetworkName { get; set; } = "taoadmin";
    public List<ManifestService> Services { get; set; } = new();

    /// <summary>生成本机 docker-compose.yml（分布式版；原生路线已移除，容器一条路）。</summary>
    public string ToComposeYaml()
    {
        if (Services.Count == 0)
            throw new InvalidOperationException($"节点 {NodeId} 部署清单为空，拒绝生成空 compose");

        var sb = new StringBuilder();
        sb.AppendLine("# <taoadmin-generated> 本文件由分布式安装器生成，重新生成会覆盖");
        sb.AppendLine("services:");
        foreach (var s in Services)
        {
            sb.AppendLine($"  {s.Name}:");
            sb.AppendLine($"    image: {s.Image}");
            sb.AppendLine($"    container_name: {s.Name}");
            if (s.RestartAlways)
                sb.AppendLine("    restart: always");
            if (s.HostPort is int hp)
            {
                sb.AppendLine("    ports:");
                sb.AppendLine($"      - \"{hp}:{s.ContainerPort}\"");
            }
            if (s.Env.Count > 0)
            {
                sb.AppendLine("    environment:");
                foreach (var e in s.Env)
                    sb.AppendLine($"      - {e}");
            }
            if (s.Volumes.Count > 0)
            {
                sb.AppendLine("    volumes:");
                foreach (var v in s.Volumes)
                    sb.AppendLine($"      - {v}");
            }
            if (s.HealthPath is not null)
            {
                sb.AppendLine("    healthcheck:");
                sb.AppendLine($"      test: [\"CMD-SHELL\", \"{s.HealthcheckTest}\"]");
                sb.AppendLine("      interval: 15s");
                sb.AppendLine("      timeout: 5s");
                sb.AppendLine("      retries: 5");
            }
            sb.AppendLine("    extra_hosts:");
            sb.AppendLine("      - \"host.docker.internal:host-gateway\"");
            sb.AppendLine("    networks:");
            sb.AppendLine($"      - {NetworkName}");
        }
        sb.AppendLine($"networks:");
        sb.AppendLine($"  {NetworkName}:");
        sb.AppendLine($"    name: {NetworkName}");
        sb.AppendLine($"    external: false");
        return sb.ToString();
    }
}

/// <summary>
/// 集群部署清单：全部机器的服务分配 + 跨机网络配置。
/// 由拓扑+角色规格构建，可序列化为 JSON 作为代理任务载荷下发。
/// </summary>
public sealed class DeploymentManifest
{
    /// <summary>发布端口起始（每服务顺延一个，避免同机冲突）。</summary>
    public const int BaseHostPort = 8000;

    public string ClusterName { get; set; } = "taoadmin";
    public List<NodeManifest> Nodes { get; set; } = new();

    /// <summary>
    /// 从拓扑构建部署清单：每个 Placement 生成一个服务项；
    /// 发布端口从 BaseHostPort 顺延；应用/网关/前端服务注入跨机中间件地址环境变量。
    /// </summary>
    public static DeploymentManifest Build(ClusterTopology topo, IReadOnlyDictionary<string, ServiceSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(topo);
        ArgumentNullException.ThrowIfNull(specs);

        // 规格覆盖校验：每个角色必须有规格（镜像不能凭空变出来）
        foreach (var role in topo.Roles)
            if (!specs.ContainsKey(role.Id))
                throw new InvalidOperationException($"角色 {role.Id} 缺少部署规格（镜像）");

        // 中间件落点：跨机地址注入用（取承载该中间件第一台机器的 Host）
        string? HostOf(ServiceKind kind) =>
            topo.Placements.Where(p => topo.Roles.First(r => r.Id == p.RoleId).Kind == kind)
                .Select(p => topo.Nodes.First(n => n.Id == p.NodeId).Host)
                .FirstOrDefault();

        var dbHost = HostOf(ServiceKind.Database);
        var cacheHost = HostOf(ServiceKind.Cache);
        var queueHost = HostOf(ServiceKind.Queue);

        var manifest = new DeploymentManifest { ClusterName = "taoadmin" };
        var nextPort = BaseHostPort;

        foreach (var node in topo.Nodes)
        {
            var nm = new NodeManifest { NodeId = node.Id };
            foreach (var p in topo.Placements.Where(p => p.NodeId == node.Id).OrderBy(p => p.RoleId).ThenBy(p => p.InstanceIndex))
            {
                var role = topo.Roles.First(r => r.Id == p.RoleId);
                var spec = specs[p.RoleId];
                if (string.IsNullOrWhiteSpace(spec.Image))
                    throw new InvalidOperationException($"角色 {role.Id} 镜像为空");

                var svc = new ManifestService
                {
                    RoleId = role.Id,
                    Name = $"{role.Id}-{p.InstanceIndex}",
                    Image = spec.Image,
                    ContainerPort = spec.ContainerPort,
                    HostPort = nextPort++,
                    HealthPath = spec.HealthPath,
                    Env = spec.Env?.ToList() ?? new List<string>(),
                    Volumes = spec.Volumes?.ToList() ?? new List<string>(),
                };

                // 跨机中间件地址注入：仅注入到非中间件服务（中间件自身不需要找自己）
                if (!role.IsMiddleware)
                {
                    if (dbHost is not null) svc.Env.Add($"TAO_DB_HOST={dbHost}");
                    if (cacheHost is not null) svc.Env.Add($"TAO_CACHE_HOST={cacheHost}");
                    if (queueHost is not null) svc.Env.Add($"TAO_QUEUE_HOST={queueHost}");
                }
                nm.Services.Add(svc);
            }
            manifest.Nodes.Add(nm);
        }

        manifest.Validate();
        return manifest;
    }

    /// <summary>取某机的清单（中控按机下发任务载荷）。</summary>
    public NodeManifest ForNode(string nodeId) =>
        Nodes.FirstOrDefault(n => n.NodeId == nodeId)
        ?? throw new InvalidOperationException($"部署清单不含节点：{nodeId}");

    /// <summary>清单校验：镜像非空、同机发布端口不冲突、服务名不重复。</summary>
    public void Validate()
    {
        foreach (var node in Nodes)
        {
            var ports = new HashSet<int>();
            var names = new HashSet<string>();
            foreach (var s in node.Services)
            {
                if (string.IsNullOrWhiteSpace(s.Image))
                    throw new InvalidOperationException($"节点 {node.NodeId} 服务 {s.Name} 镜像为空");
                if (string.IsNullOrWhiteSpace(s.Name))
                    throw new InvalidOperationException($"节点 {node.NodeId} 存在无名服务");
                if (!names.Add(s.Name))
                    throw new InvalidOperationException($"节点 {node.NodeId} 服务名重复：{s.Name}");
                if (s.HostPort is int hp && !ports.Add(hp))
                    throw new InvalidOperationException($"节点 {node.NodeId} 发布端口冲突：{hp}（{s.Name}）");
            }
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static DeploymentManifest FromJson(string json) =>
        JsonSerializer.Deserialize<DeploymentManifest>(json)
        ?? throw new InvalidOperationException("部署清单反序列化为空");
}
