using System.Text.Json;
using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

/// <summary>
/// 代理端（AgentService）实效断言：四阶段顺序执行、坏阶段停住、健康重试、
/// 未知任务拒绝、进度上报与结果回传、心跳、清单载荷落盘 compose。
/// </summary>
public class AgentServiceTests
{
    private sealed class FakeRunner : IAgentCommandRunner
    {
        public List<string> Commands { get; } = new();
        /// <summary>按命令前缀决定退出码；未命中默认 0。</summary>
        public Func<string, int> Responder { get; set; } = _ => 0;

        public Task<(int ExitCode, List<string> Output)> RunAsync(string command, CancellationToken ct)
        {
            Commands.Add(command);
            var code = Responder(command);
            return Task.FromResult((code, new List<string> { $"out:{command}" }));
        }
    }

    private sealed class FakeReporter : IAgentReporter
    {
        public List<AgentProgress> Progress { get; } = new();
        public List<AgentTaskResult> Results { get; } = new();
        public Task ReportProgressAsync(AgentProgress p, CancellationToken ct) { Progress.Add(p); return Task.CompletedTask; }
        public Task ReportResultAsync(AgentTaskResult r, CancellationToken ct) { Results.Add(r); return Task.CompletedTask; }
    }

    private static (AgentService Svc, FakeRunner Runner, FakeReporter Reporter) Create(string workDir, int healthRetries = 3)
        => Create(workDir, new FakeRunner(), healthRetries);

    private static (AgentService Svc, FakeRunner Runner, FakeReporter Reporter) Create(string workDir, FakeRunner runner, int healthRetries = 3)
    {
        var reporter = new FakeReporter();
        var svc = new AgentService(runner, reporter, new AgentOptions
        {
            NodeId = "node-1",
            WorkDir = workDir,
            HealthRetries = healthRetries,
            HealthRetryDelayMs = 0
        });
        return (svc, runner, reporter);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static NodeManifest SampleManifest() => new()
    {
        NodeId = "node-1",
        Services = new List<ManifestService>
        {
            new() { RoleId = "app", Name = "app-0", Image = "taoadmin/app:1.0", HostPort = 8000, ContainerPort = 8080, HealthPath = "/health" }
        }
    };

    [Fact]
    public async Task Install_ShouldRunFourPhasesInOrder_AndReportProgress()
    {
        var dir = TempDir();
        var (svc, runner, reporter) = Create(dir);
        var task = new AgentTask { TaskId = "t1", Kind = "install", Payload = JsonSerializer.Serialize(SampleManifest()) };

        var result = await svc.ExecuteAsync(task);

        Assert.True(result.Success);
        // 四阶段命令按序出现：docker info → pull → up -d → curl
        Assert.Contains(runner.Commands, c => c == "docker info");
        Assert.Contains(runner.Commands, c => c.Contains("compose") && c.Contains("pull"));
        Assert.Contains(runner.Commands, c => c.Contains("compose") && c.Contains("up -d"));
        Assert.Contains(runner.Commands, c => c.StartsWith("curl -fsS http://127.0.0.1:8000/health"));
        var pullIdx = runner.Commands.FindIndex(c => c.Contains("pull"));
        var upIdx = runner.Commands.FindIndex(c => c.Contains("up -d"));
        var curlIdx = runner.Commands.FindIndex(c => c.StartsWith("curl"));
        Assert.True(pullIdx < upIdx && upIdx < curlIdx, "阶段顺序应为 pull → start → health");

        // 进度上报：每阶段都有，最终 100
        Assert.Equal(100, reporter.Progress.Max(p => p.Percent));
        Assert.All(reporter.Progress, p => Assert.Equal("node-1", p.NodeId));
        // 结果回传中控
        Assert.Single(reporter.Results);
        Assert.True(reporter.Results[0].Success);
        // 本地账本同步可读
        Assert.NotNull(svc.Ledger.Get("node-1"));
    }

    [Fact]
    public async Task Install_PhaseFailure_ShouldStop_AndReportFailure()
    {
        var dir = TempDir();
        var runner = new FakeRunner { Responder = cmd => cmd.Contains("pull") ? 1 : 0 };
        var (svc, _, reporter) = Create(dir, runner);
        var task = new AgentTask { TaskId = "t2", Kind = "install", Payload = JsonSerializer.Serialize(SampleManifest()) };

        var result = await svc.ExecuteAsync(task);

        Assert.False(result.Success);
        Assert.Contains("pull", result.Error);
        // 坏阶段停住：pull 失败后不再有 up -d 和 curl
        Assert.DoesNotContain(runner.Commands, c => c.Contains("up -d"));
        Assert.DoesNotContain(runner.Commands, c => c.StartsWith("curl"));
        Assert.Single(reporter.Results);
        Assert.False(reporter.Results[0].Success);
    }

    [Fact]
    public async Task HealthCheck_ShouldRetryUntilSuccess()
    {
        var dir = TempDir();
        var attempts = 0;
        var runner = new FakeRunner { Responder = cmd => cmd.StartsWith("curl") ? (attempts++ < 2 ? 1 : 0) : 0 };
        var (svc, _, _) = Create(dir, runner, healthRetries: 3);
        var task = new AgentTask { TaskId = "t3", Kind = "health", Payload = JsonSerializer.Serialize(SampleManifest()) };

        var result = await svc.ExecuteAsync(task);

        Assert.True(result.Success);
        Assert.Equal(3, attempts); // 前两次失败，第三次成功
    }

    [Fact]
    public async Task HealthCheck_AllRetriesFailed_ShouldFail()
    {
        var dir = TempDir();
        var runner = new FakeRunner { Responder = cmd => cmd.StartsWith("curl") ? 1 : 0 };
        var (svc, _, _) = Create(dir, runner, healthRetries: 2);
        var task = new AgentTask { TaskId = "t4", Kind = "health", Payload = JsonSerializer.Serialize(SampleManifest()) };

        var result = await svc.ExecuteAsync(task);

        Assert.False(result.Success);
        Assert.Contains("2/2", result.Error); // 重试次数打满
    }

    [Fact]
    public async Task UnknownKind_ShouldFail_NotThrow()
    {
        var dir = TempDir();
        var (svc, _, reporter) = Create(dir);
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t5", Kind = "reboot-everything" });

        Assert.False(result.Success);
        Assert.Contains("未知任务类型", result.Error);
        Assert.Single(reporter.Results); // 失败也回传中控
    }

    [Fact]
    public async Task Upgrade_ShouldSkipEnvPhase()
    {
        var dir = TempDir();
        var (svc, runner, _) = Create(dir);
        await svc.ExecuteAsync(new AgentTask { TaskId = "t6", Kind = "upgrade", Payload = JsonSerializer.Serialize(SampleManifest()) });

        Assert.DoesNotContain(runner.Commands, c => c == "docker info"); // 升级不重复装环境
        Assert.Contains(runner.Commands, c => c.Contains("pull"));
        Assert.Contains(runner.Commands, c => c.Contains("up -d"));
    }

    [Fact]
    public async Task Stop_ShouldOnlyStop()
    {
        var dir = TempDir();
        var (svc, runner, _) = Create(dir);
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t7", Kind = "stop", Payload = JsonSerializer.Serialize(SampleManifest()) });

        Assert.True(result.Success);
        Assert.Contains(runner.Commands, c => c.Contains("compose") && c.Contains("stop"));
        Assert.DoesNotContain(runner.Commands, c => c.Contains("up -d"));
        Assert.DoesNotContain(runner.Commands, c => c.StartsWith("curl"));
    }

    [Fact]
    public async Task ManifestPayload_ShouldBeWrittenAsComposeFile()
    {
        var dir = TempDir();
        var (svc, _, _) = Create(dir);
        await svc.ExecuteAsync(new AgentTask { TaskId = "t8", Kind = "install", Payload = JsonSerializer.Serialize(SampleManifest()) });

        var compose = Path.Combine(dir, "docker-compose.yml");
        Assert.True(File.Exists(compose));
        var text = File.ReadAllText(compose);
        Assert.Contains("taoadmin/app:1.0", text);
        Assert.Contains("<taoadmin-generated>", text);
    }

    [Fact]
    public async Task Heartbeat_ShouldReportAlive()
    {
        var dir = TempDir();
        var (svc, _, reporter) = Create(dir);
        await svc.SendHeartbeatAsync();

        var p = Assert.Single(reporter.Progress);
        Assert.Equal("heartbeat", p.Phase);
        Assert.Equal("node-1", p.NodeId);
    }

    [Fact]
    public async Task MissingHealthTarget_ShouldPassHealthPhase()
    {
        // 本机没有配健康路径的服务：健康阶段视为通过（无服务可探，不误报）
        var dir = TempDir();
        var (svc, _, _) = Create(dir);
        var manifest = new NodeManifest
        {
            NodeId = "node-1",
            Services = new List<ManifestService> { new() { RoleId = "app", Name = "app-0", Image = "taoadmin/app:1.0", HostPort = 8000, ContainerPort = 8080 } }
        };
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t9", Kind = "install", Payload = JsonSerializer.Serialize(manifest) });

        Assert.True(result.Success);
    }

    [Fact]
    public void Constructor_BlankNodeId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentService(new FakeRunner(), new FakeReporter(), new AgentOptions { NodeId = "" }));
    }
}

/// <summary>
/// 部署清单实效断言：拓扑→每机清单、compose 生成、跨机中间件地址注入、
/// 端口冲突/镜像为空校验、JSON 往返（中控下发载荷口径）。
/// </summary>
public class DeploymentManifestTests
{
    private static ClusterTopology SampleTopology()
    {
        var topo = new ClusterTopology
        {
            Nodes = new List<ClusterNode>
            {
                new() { Id = "n1", Name = "数据机", Host = "10.0.0.1" },
                new() { Id = "n2", Name = "应用机", Host = "10.0.0.2" },
            },
            Roles = new List<ServiceRole>
            {
                new() { Id = "mysql", Name = "MySQL", Kind = ServiceKind.Database },
                new() { Id = "redis", Name = "Redis", Kind = ServiceKind.Cache },
                new() { Id = "gateway", Name = "网关", Kind = ServiceKind.Gateway },
                new() { Id = "app", Name = "应用", Kind = ServiceKind.AppService, Instances = 2 },
            }
        };
        topo.Placements = PlacementPlanner.Recommend(topo);
        return topo;
    }

    private static IReadOnlyDictionary<string, ServiceSpec> Specs() => new Dictionary<string, ServiceSpec>
    {
        ["mysql"] = new ServiceSpec("mysql:8.0", 3306, HealthPath: "/ping"),
        ["redis"] = new ServiceSpec("redis:7", 6379),
        ["gateway"] = new ServiceSpec("taoadmin/gateway:1.0", 8080, HealthPath: "/health"),
        ["app"] = new ServiceSpec("taoadmin/app:1.0", 8080, HealthPath: "/health"),
    };

    [Fact]
    public void Build_ShouldCoverAllPlacements()
    {
        var topo = SampleTopology();
        var manifest = DeploymentManifest.Build(topo, Specs());

        Assert.Equal(2, manifest.Nodes.Count);
        var total = manifest.Nodes.Sum(n => n.Services.Count);
        Assert.Equal(topo.Placements.Count, total);

        // 推荐布局口径：mysql（第一个中间件）落数据机 n1，redis 落 n2；网关落 n2；应用实例轮转均摊
        var n1 = manifest.ForNode("n1");
        Assert.Contains(n1.Services, s => s.RoleId == "mysql");
        var n2 = manifest.ForNode("n2");
        Assert.Contains(n2.Services, s => s.RoleId == "redis");
        Assert.Contains(n2.Services, s => s.RoleId == "gateway");
        // 应用 2 实例轮转两台机
        Assert.Contains(n1.Services, s => s.RoleId == "app");
        Assert.Contains(n2.Services, s => s.RoleId == "app");
    }

    [Fact]
    public void Build_ShouldInjectCrossNodeMiddlewareHosts()
    {
        var topo = SampleTopology();
        var manifest = DeploymentManifest.Build(topo, Specs());

        // 应用/网关服务注入中间件地址（mysql 在 n1、redis 在 n2，按实际落点注入）
        var appSvc = manifest.ForNode("n2").Services.First(s => s.RoleId == "app");
        Assert.Contains("TAO_DB_HOST=10.0.0.1", appSvc.Env);
        Assert.Contains("TAO_CACHE_HOST=10.0.0.2", appSvc.Env);

        // 中间件自身不注入（不找自己）
        var mysqlSvc = manifest.ForNode("n1").Services.First(s => s.RoleId == "mysql");
        Assert.DoesNotContain(mysqlSvc.Env, e => e.StartsWith("TAO_DB_HOST="));
    }

    [Fact]
    public void Build_ShouldAssignUniqueHostPorts()
    {
        var topo = SampleTopology();
        var manifest = DeploymentManifest.Build(topo, Specs());

        foreach (var node in manifest.Nodes)
        {
            var ports = node.Services.Select(s => s.HostPort!.Value).ToList();
            Assert.Equal(ports.Count, ports.Distinct().Count()); // 同机端口不冲突
            Assert.All(ports, p => Assert.True(p >= DeploymentManifest.BaseHostPort));
        }
    }

    [Fact]
    public void ToComposeYaml_ShouldContainServiceAndNetwork()
    {
        var topo = SampleTopology();
        var manifest = DeploymentManifest.Build(topo, Specs());
        var yaml = manifest.ForNode("n1").ToComposeYaml();

        Assert.Contains("image: mysql:8.0", yaml);
        Assert.Contains("restart: always", yaml);
        Assert.Contains("healthcheck:", yaml);
        Assert.Contains("host.docker.internal:host-gateway", yaml);
        Assert.Contains("networks:", yaml);
        Assert.Contains("taoadmin", yaml);
        Assert.Contains("<taoadmin-generated>", yaml);
    }

    [Fact]
    public void ToComposeYaml_EmptyNode_ShouldThrow()
    {
        var nm = new NodeManifest { NodeId = "empty" };
        Assert.Throws<InvalidOperationException>(() => nm.ToComposeYaml());
    }

    [Fact]
    public void Build_MissingSpec_ShouldThrow()
    {
        var topo = SampleTopology();
        var specs = new Dictionary<string, ServiceSpec>(Specs());
        specs.Remove("mysql");
        Assert.Throws<InvalidOperationException>(() => DeploymentManifest.Build(topo, specs));
    }

    [Fact]
    public void Validate_DuplicatePort_ShouldThrow()
    {
        var manifest = new DeploymentManifest
        {
            Nodes = new List<NodeManifest>
            {
                new()
                {
                    NodeId = "n1",
                    Services = new List<ManifestService>
                    {
                        new() { Name = "a", Image = "img:a", HostPort = 8000 },
                        new() { Name = "b", Image = "img:b", HostPort = 8000 },
                    }
                }
            }
        };
        Assert.Throws<InvalidOperationException>(() => manifest.Validate());
    }

    [Fact]
    public void Validate_BlankImage_ShouldThrow()
    {
        var manifest = new DeploymentManifest
        {
            Nodes = new List<NodeManifest>
            {
                new() { NodeId = "n1", Services = new List<ManifestService> { new() { Name = "a", Image = "" } } }
            }
        };
        Assert.Throws<InvalidOperationException>(() => manifest.Validate());
    }

    [Fact]
    public void JsonRoundTrip_ShouldPreserveManifest()
    {
        var topo = SampleTopology();
        var manifest = DeploymentManifest.Build(topo, Specs());

        var json = manifest.ToJson();
        var back = DeploymentManifest.FromJson(json);

        Assert.Equal(manifest.Nodes.Count, back.Nodes.Count);
        Assert.Equal(
            manifest.ForNode("n2").Services.First(s => s.RoleId == "app").Env.OrderBy(e => e),
            back.ForNode("n2").Services.First(s => s.RoleId == "app").Env.OrderBy(e => e));
        // JSON 载荷可直接作为代理任务 Payload 下发
        var task = new AgentTask { TaskId = "t", Kind = "install", Payload = json };
        Assert.False(string.IsNullOrWhiteSpace(task.Payload));
    }

    [Fact]
    public void ForNode_UnknownNode_ShouldThrow()
    {
        var topo = SampleTopology();
        var manifest = DeploymentManifest.Build(topo, Specs());
        Assert.Throws<InvalidOperationException>(() => manifest.ForNode("no-such-node"));
    }
}
