// ⑳分布式模块覆盖补账（9.11 百分百全覆盖令）：逐条补实效断言用例，
// 每条用例至少一条可失败断言；防御性分支经重构消除或注入探针真实测到。
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

// ==================== AgentService 缺口 ====================

public class AgentServiceGapTests
{
    private sealed class FakeRunner : IAgentCommandRunner
    {
        public List<string> Commands { get; } = new();
        public Func<string, int> Responder { get; set; } = _ => 0;
        public Exception? ThrowOnRun { get; set; }

        public Task<(int ExitCode, List<string> Output)> RunAsync(string command, CancellationToken ct)
        {
            Commands.Add(command);
            if (ThrowOnRun is not null) throw ThrowOnRun;
            return Task.FromResult((Responder(command), new List<string> { $"out:{command}" }));
        }
    }

    private sealed class FakeReporter : IAgentReporter
    {
        public List<AgentProgress> Progress { get; } = new();
        public List<AgentTaskResult> Results { get; } = new();
        public Task ReportProgressAsync(AgentProgress p, CancellationToken ct) { Progress.Add(p); return Task.CompletedTask; }
        public Task ReportResultAsync(AgentTaskResult r, CancellationToken ct) { Results.Add(r); return Task.CompletedTask; }
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (AgentService Svc, FakeRunner Runner, FakeReporter Reporter) Create(
        FakeRunner? runner = null, Action<AgentOptions>? opt = null, string? workDir = null)
    {
        var options = new AgentOptions { NodeId = "node-1", WorkDir = workDir ?? TempDir(), HealthRetryDelayMs = 0 };
        opt?.Invoke(options);
        runner ??= new FakeRunner();
        var reporter = new FakeReporter();
        return (new AgentService(runner, reporter, options), runner, reporter);
    }

    private static string ManifestJson() => JsonSerializer.Serialize(new NodeManifest
    {
        NodeId = "node-1",
        Services = new List<ManifestService>
        {
            new() { RoleId = "app", Name = "app-0", Image = "taoadmin/app:1.0", HostPort = 8000, ContainerPort = 8080, HealthPath = "/health" }
        }
    });

    [Fact]
    public void Constructor_NullRunner_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentService(null!, new FakeReporter(), new AgentOptions { NodeId = "n" }));
    }

    [Fact]
    public void Constructor_NullReporter_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentService(new FakeRunner(), null!, new AgentOptions { NodeId = "n" }));
    }

    [Fact]
    public void Constructor_NullOptions_ShouldThrow_NodeIdBlank()
    {
        // opt=null 走默认选项分支，NodeId 为空被参数校验拦下
        var ex = Assert.Throws<ArgumentException>(() => new AgentService(new FakeRunner(), new FakeReporter(), null));
        Assert.Contains("NodeId 不能为空", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsync_BlankTaskId_ShouldThrow()
    {
        var (svc, _, reporter) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.ExecuteAsync(new AgentTask { TaskId = "  ", Kind = "install" }));
        Assert.Empty(reporter.Results); // 参数非法直接拒绝，不产生任务回执
    }

    [Fact]
    public async Task ExecuteAsync_StartKind_ShouldRunStartThenHealth()
    {
        var (svc, runner, _) = Create();
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t-start", Kind = "start", Payload = ManifestJson() });

        Assert.True(result.Success);
        Assert.Contains(runner.Commands, c => c.Contains("up -d"));
        Assert.Contains(runner.Commands, c => c.StartsWith("curl"));
        Assert.DoesNotContain(runner.Commands, c => c == "docker info"); // start 不重复装环境
        Assert.DoesNotContain(runner.Commands, c => c.Contains("pull"));
    }

    [Fact]
    public async Task ExecuteAsync_Canceled_ShouldRethrow_WithoutReporting()
    {
        var runner = new FakeRunner { ThrowOnRun = new OperationCanceledException("ctrl-c") };
        var (svc, _, reporter) = Create(runner);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            svc.ExecuteAsync(new AgentTask { TaskId = "t-cancel", Kind = "install", Payload = ManifestJson() }));

        Assert.Empty(reporter.Results); // 取消不产生结果回执（中控按取消口径处理）
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJsonPayload_ShouldFailWithParseError()
    {
        var (svc, _, _) = Create();
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t-bad", Kind = "install", Payload = "{not-json" });

        Assert.False(result.Success);
        Assert.Contains("部署清单载荷解析失败", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NullJsonPayload_ShouldSkipCompose_AndSucceed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "agent-gap-null-" + Guid.NewGuid().ToString("N"));
        var (svc, _, _) = Create(workDir: dir);
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t-null", Kind = "install", Payload = "null" });

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(dir, "docker-compose.yml"))); // 空清单不落 compose
    }

    [Fact]
    public async Task ExecuteAsync_BlankPayload_ShouldSkipCompose_AndSucceed()
    {
        // 空白载荷：PrepareComposeFile 直接返回 null，不解析不落盘，四阶段照常跑
        var dir = Path.Combine(Path.GetTempPath(), "agent-gap-blank-" + Guid.NewGuid().ToString("N"));
        var (svc, _, _) = Create(workDir: dir);
        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t-blank", Kind = "install", Payload = "   " });

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(dir, "docker-compose.yml")));
    }

    [Fact]
    public async Task ExecuteAsync_HealthFailsInPhasedRun_ShouldStopAtHealth()
    {
        // 分阶段任务中健康检查失败：带原因停住，不报成功
        var runner = new FakeRunner { Responder = cmd => cmd.StartsWith("curl") ? 1 : 0 };
        var (svc, _, _) = Create(runner, opt => { opt.HealthRetries = 1; opt.HealthRetryDelayMs = 0; });

        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t-health-fail", Kind = "start", Payload = ManifestJson() });

        Assert.False(result.Success);
        Assert.Contains("健康检查", result.Error);
    }

    [Fact]
    public async Task HealthCheck_DelayBetweenRetries_ShouldSleepAndRecover()
    {
        var attempts = 0;
        var runner = new FakeRunner { Responder = cmd => cmd.StartsWith("curl") ? (attempts++ == 0 ? 1 : 0) : 0 };
        var (svc, _, _) = Create(runner, opt => { opt.HealthRetries = 3; opt.HealthRetryDelayMs = 10; });

        var result = await svc.ExecuteAsync(new AgentTask { TaskId = "t-delay", Kind = "health", Payload = ManifestJson() });

        Assert.True(result.Success);
        Assert.Equal(2, attempts); // 首次失败带间隔重试，第二次成功
    }

    [Fact]
    public void CommandFor_UnknownPhase_ShouldThrow()
    {
        var (svc, _, _) = Create();
        var ex = Assert.Throws<InvalidOperationException>(() => svc.CommandFor("reboot", null));
        Assert.Contains("未知阶段", ex.Message);
    }
}

// ==================== AgentHttpClient / AgentNodeExecutor 缺口 ====================

public class AgentHttpChannelGapTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        public Exception? ThrowOnSend { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (ThrowOnSend is not null) throw ThrowOnSend;
            return Task.FromResult(Responder(request));
        }
    }

    [Fact]
    public void Constructor_DefaultHttpClient_ShouldBeCreated()
    {
        var client = new AgentHttpClient();
        Assert.NotNull(client);
    }

    [Fact]
    public async Task PostTaskAsync_ShouldPostToTasks_AndParseResult()
    {
        var handler = new StubHandler
        {
            Responder = req =>
            {
                Assert.EndsWith("/tasks", req.RequestUri!.AbsolutePath);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"taskId":"t1","success":true}""", Encoding.UTF8, "application/json")
                };
            }
        };
        var client = new AgentHttpClient(new HttpClient(handler));

        var result = await client.PostTaskAsync("http://127.0.0.1:5400/", new AgentTask { TaskId = "t1", Kind = "install" }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("t1", result.TaskId);
    }

    [Fact]
    public async Task PostTaskAsync_NullBody_ShouldThrow()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") } };
        var client = new AgentHttpClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.PostTaskAsync("http://127.0.0.1:5400", new AgentTask { TaskId = "t1" }, CancellationToken.None));
        Assert.Contains("任务结果为空", ex.Message);
    }

    [Fact]
    public async Task GetProgressAsync_ShouldParseProgress()
    {
        var handler = new StubHandler
        {
            Responder = req =>
            {
                Assert.Contains("nodeId=n1", req.RequestUri!.Query);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"nodeId":"n1","phase":"pull","percent":50}""", Encoding.UTF8, "application/json")
                };
            }
        };
        var client = new AgentHttpClient(new HttpClient(handler));

        var p = await client.GetProgressAsync("http://127.0.0.1:5400", "n1", CancellationToken.None);

        Assert.NotNull(p);
        Assert.Equal("pull", p!.Phase);
        Assert.Equal(50, p.Percent);
    }

    [Fact]
    public async Task GetProgressAsync_NonSuccess_ShouldReturnNull()
    {
        var handler = new StubHandler { Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
        var client = new AgentHttpClient(new HttpClient(handler));

        var p = await client.GetProgressAsync("http://127.0.0.1:5400", "n1", CancellationToken.None);

        Assert.Null(p);
    }

    [Fact]
    public void Executor_DefaultHttpClient_ShouldBeCreated()
    {
        var executor = new AgentNodeExecutor();
        Assert.NotNull(executor);
    }

    [Fact]
    public async Task PollProgress_Canceled_ShouldRethrow()
    {
        var http = new AgentHttpChannelGapTestsStub { ThrowOnPoll = new OperationCanceledException("cancel") };
        var executor = new AgentNodeExecutor(http);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.PollProgressAsync(new ClusterNode { Id = "n1", Host = "10.0.0.1" }, new NodeProgressLedger(), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteTask_Canceled_ShouldRethrow_NotFailResult()
    {
        // 下发任务被取消：原样抛 OCE，不转失败结果（取消口径统一在编排层）
        var http = new AgentHttpChannelGapTestsStub { ThrowOnPost = new OperationCanceledException("cancel") };
        var executor = new AgentNodeExecutor(http);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(new ClusterNode { Id = "n1", Host = "10.0.0.1" },
                new AgentTask { TaskId = "t1", Kind = "install" }, new NodeProgressLedger(), CancellationToken.None));
    }

    private sealed class AgentHttpChannelGapTestsStub : IAgentHttpClient
    {
        public Exception? ThrowOnPoll { get; set; }
        public Exception? ThrowOnPost { get; set; }
        public Task<AgentTaskResult> PostTaskAsync(string baseUrl, AgentTask task, CancellationToken ct)
        {
            if (ThrowOnPost is not null) throw ThrowOnPost;
            return Task.FromResult(new AgentTaskResult { TaskId = task.TaskId, Success = true });
        }
        public Task<AgentProgress?> GetProgressAsync(string baseUrl, string nodeId, CancellationToken ct)
        {
            if (ThrowOnPoll is not null) throw ThrowOnPoll;
            return Task.FromResult<AgentProgress?>(null);
        }
    }
}

// ==================== ClusterInstallViewModel 缺口 ====================

public class ClusterInstallViewModelGapTests
{
    private static ClusterTopology Topo() => new()
    {
        Nodes = new List<ClusterNode>
        {
            new() { Id = "n1", Name = "一", Host = "10.0.0.1", State = NodeState.Online },
            new() { Id = "n2", Name = "二", Host = "10.0.0.2", State = NodeState.Online },
        },
    };

    [Fact]
    public void DisplayPhases_ShouldBeFourPhasesInOrder()
    {
        Assert.Equal(new[] { "env", "pull", "start", "health" }, ClusterInstallViewModel.DisplayPhases);
    }

    [Fact]
    public void OverallPercent_NoRows_ShouldBeZero()
    {
        Assert.Equal(0, new ClusterInstallViewModel().OverallPercent);
    }

    [Fact]
    public void PhaseText_ShouldMapAllPhases()
    {
        Assert.Equal("装环境", new NodeProgressRow { Phase = "env" }.PhaseText);
        Assert.Equal("拉镜像", new NodeProgressRow { Phase = "pull" }.PhaseText);
        Assert.Equal("起服务", new NodeProgressRow { Phase = "start" }.PhaseText);
        Assert.Equal("健康检查", new NodeProgressRow { Phase = "health" }.PhaseText);
        Assert.Equal("停服", new NodeProgressRow { Phase = "stop" }.PhaseText);
        Assert.Equal("在线", new NodeProgressRow { Phase = "heartbeat" }.PhaseText);
        Assert.Equal("待开始", new NodeProgressRow { Phase = "" }.PhaseText);
        Assert.Equal("weird", new NodeProgressRow { Phase = "weird" }.PhaseText); // 未知阶段原样透出
    }

    [Fact]
    public void ApplyProgress_StartAndHealth_ShouldAdvance()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(Topo());

        vm.ApplyProgress(new AgentProgress { NodeId = "n1", Phase = "start", Percent = 75, Message = "起服务" });
        var row = vm.Rows.First(r => r.NodeId == "n1");
        Assert.Equal("起服务", row.PhaseText);
        Assert.Equal(75, row.Percent);

        vm.ApplyProgress(new AgentProgress { NodeId = "n1", Phase = "health", Percent = 100, Message = "健康检查" });
        Assert.Equal("健康检查", row.PhaseText);
        Assert.Equal(100, row.Percent);
    }

    [Fact]
    public void ApplyResult_TaskIdWithoutColon_ShouldMatchRow()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(Topo());
        vm.ApplyResult(new AgentTaskResult { TaskId = "n2", Success = true });

        var row = vm.Rows.First(r => r.NodeId == "n2");
        Assert.Equal(100, row.Percent);
    }

    [Fact]
    public void ApplyResult_FailureWithoutError_ShouldUseDefaultMessage()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(Topo());
        vm.ApplyResult(new AgentTaskResult { TaskId = "task:n1", Success = false, Error = null });

        var row = vm.Rows.First(r => r.NodeId == "n1");
        Assert.True(row.Failed);
        Assert.Equal("安装失败", row.Message);
    }

    [Fact]
    public void HaEnabled_ShouldBeSettable()
    {
        var topo = new ClusterTopology { HaEnabled = true };
        Assert.True(topo.HaEnabled);
    }
}

// ==================== ClusterOrchestrator 缺口 ====================

public class ClusterOrchestratorGapTests
{
    private sealed class NoopExecutor : INodeExecutor
    {
        public Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct)
            => Task.FromResult(new AgentTaskResult { TaskId = task.TaskId, Success = true });
    }

    [Fact]
    public void Constructor_ExplicitLedger_ShouldBeUsed()
    {
        var ledger = new NodeProgressLedger();
        var orch = new ClusterOrchestrator(new NoopExecutor(), ledger);
        Assert.Same(ledger, orch.Ledger);
    }

    [Fact]
    public void Constructor_DefaultLedger_ShouldBeCreated_AndUsable()
    {
        // ledger 缺省时自建账本，且真实可写入进度
        var orch = new ClusterOrchestrator(new NoopExecutor());
        Assert.NotNull(orch.Ledger);
        orch.Ledger.Update(new AgentProgress { NodeId = "n1", Phase = "env", Percent = 25, Message = "装环境" });
    }
}

// ==================== CredentialVault 缺口 ====================

public class CredentialVaultGapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vault-gap-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_NullSecret_ShouldThrow()
    {
        var vault = new CredentialVault(_dir);
        Assert.Throws<ArgumentException>(() => vault.Store("n1", null!));
    }

    [Fact]
    public void Constructor_BlankVaultDir_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => new CredentialVault(""));
        Assert.Throws<ArgumentException>(() => new CredentialVault("   "));
        Assert.Throws<ArgumentException>(() => new CredentialVault(null!));
    }

    [Fact]
    public void Delete_BlankNodeId_ShouldReturnFalse()
    {
        var vault = new CredentialVault(_dir);
        Assert.False(vault.Delete(null!));
        Assert.False(vault.Delete(""));
    }

    [Fact]
    public void Constructor_ExplicitMasterKey_ShouldRoundTrip()
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var vault = new CredentialVault(_dir, key);
        vault.Store("n1", "pw-显式密钥");
        Assert.Equal("pw-显式密钥", vault.Retrieve("n1"));
    }

    [Fact]
    public void ExistingKeyFile_ShouldBeReused()
    {
        var keyFile = Path.Combine(_dir, "vault.key");
        Directory.CreateDirectory(_dir);
        var existing = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, existing);

        var vault = new CredentialVault(_dir);
        vault.Store("n1", "pw");

        Assert.Equal(existing, File.ReadAllBytes(keyFile)); // 既有密钥不被覆盖
        var second = new CredentialVault(_dir);
        Assert.Equal("pw", second.Retrieve("n1")); // 新实例用同一密钥可解
    }

    [Fact]
    public void ShortKeyFile_ShouldBeRegenerated()
    {
        var keyFile = Path.Combine(_dir, "vault.key");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(keyFile, new byte[8]); // 长度不对：视为损坏，重新生成

        var vault = new CredentialVault(_dir);
        vault.Store("n1", "pw");

        Assert.Equal(32, File.ReadAllBytes(keyFile).Length);
        Assert.Equal("pw", vault.Retrieve("n1"));
    }

    [Fact]
    public void Sanitize_NodeIdWithInvalidChars_ShouldTruncate()
    {
        var vault = new CredentialVault(_dir);
        vault.Store("n1/../../etc", "pw");

        Assert.True(File.Exists(Path.Combine(_dir, "n1.cred"))); // 非法字符截断，不越目录
        Assert.Equal("pw", vault.Retrieve("n1/../../etc"));
    }

    [Fact]
    public void UnixFileMode_Unsupported_ShouldSkipRestriction_AndStillWork()
    {
        // Windows 路径实测：探针返回 false 时跳过权限收紧，存取不受影响
        var vault = new CredentialVault(_dir, unixFileModeSupported: () => false);
        vault.Store("n1", "pw-windows");
        Assert.Equal("pw-windows", vault.Retrieve("n1"));
        Assert.True(File.Exists(Path.Combine(_dir, "n1.cred")));
    }

    [Fact]
    public void UnixFileMode_Supported_ShouldRestrictToOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows 无 UnixFileMode API：探针放行时真实调用必抛 PNSE（构造期密钥文件即触发）——
            // 恰好证明探针确实在闸住这条调用，而非空转
            Assert.Throws<PlatformNotSupportedException>(() =>
                new CredentialVault(_dir, unixFileModeSupported: () => true));
            return;
        }
        var vault = new CredentialVault(_dir, unixFileModeSupported: () => true);
        vault.Store("n1", "pw");

        var mode = File.GetUnixFileMode(Path.Combine(_dir, "n1.cred"));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}

// ==================== DeploymentManifest 缺口 ====================

public class DeploymentManifestGapTests
{
    [Fact]
    public void Build_BlankImage_ShouldThrow()
    {
        var topo = new ClusterTopology
        {
            Nodes = { new ClusterNode { Id = "n1", Host = "10.0.0.1" } },
            Roles = { new ServiceRole { Id = "app", Kind = ServiceKind.AppService } },
            Placements = { new Placement { RoleId = "app", InstanceIndex = 0, NodeId = "n1" } },
        };
        var specs = new Dictionary<string, ServiceSpec> { ["app"] = new ServiceSpec("", 8080) };

        var ex = Assert.Throws<InvalidOperationException>(() => DeploymentManifest.Build(topo, specs));
        Assert.Contains("镜像为空", ex.Message);
    }

    [Fact]
    public void Build_WithEnvAndVolumes_ShouldCarryToServices()
    {
        var topo = new ClusterTopology
        {
            Nodes = { new ClusterNode { Id = "n1", Host = "10.0.0.1" } },
            Roles = { new ServiceRole { Id = "app", Kind = ServiceKind.AppService } },
            Placements = { new Placement { RoleId = "app", InstanceIndex = 0, NodeId = "n1" } },
        };
        var specs = new Dictionary<string, ServiceSpec>
        {
            ["app"] = new ServiceSpec("taoadmin/app:1.0", 8080, Env: new[] { "TAO_MODE=prod" }, Volumes: new[] { "/data:/data" }),
        };

        var manifest = DeploymentManifest.Build(topo, specs);
        var svc = manifest.ForNode("n1").Services.Single();
        Assert.Contains("TAO_MODE=prod", svc.Env);
        Assert.Contains("/data:/data", svc.Volumes);
    }

    [Fact]
    public void Build_QueueMiddleware_ShouldInjectQueueHost()
    {
        var topo = new ClusterTopology
        {
            Nodes =
            {
                new ClusterNode { Id = "n1", Host = "10.0.0.1" },
                new ClusterNode { Id = "n2", Host = "10.0.0.2" },
            },
            Roles =
            {
                new ServiceRole { Id = "queue", Kind = ServiceKind.Queue },
                new ServiceRole { Id = "app", Kind = ServiceKind.AppService },
            },
            Placements =
            {
                new Placement { RoleId = "queue", InstanceIndex = 0, NodeId = "n1" },
                new Placement { RoleId = "app", InstanceIndex = 0, NodeId = "n2" },
            },
        };
        var specs = new Dictionary<string, ServiceSpec>
        {
            ["queue"] = new ServiceSpec("rabbitmq:3", 5672),
            ["app"] = new ServiceSpec("taoadmin/app:1.0", 8080),
        };

        var manifest = DeploymentManifest.Build(topo, specs);
        var appSvc = manifest.ForNode("n2").Services.Single();
        Assert.Contains("TAO_QUEUE_HOST=10.0.0.1", appSvc.Env);
    }

    [Fact]
    public void ToComposeYaml_WithVolumesAndNoRestart_ShouldRender()
    {
        var nm = new NodeManifest
        {
            NodeId = "n1",
            Services =
            {
                new ManifestService
                {
                    Name = "app-0", Image = "taoadmin/app:1.0", ContainerPort = 8080,
                    RestartAlways = false, Volumes = { "/data:/data" },
                }
            },
        };
        var yaml = nm.ToComposeYaml();

        Assert.Contains("volumes:", yaml);
        Assert.Contains("- /data:/data", yaml);
        Assert.DoesNotContain("restart: always", yaml);
    }

    [Fact]
    public void HealthcheckTest_WithoutHealthPath_ShouldBeEmpty()
    {
        var svc = new ManifestService { Name = "a", Image = "img", ContainerPort = 8080 };
        Assert.Equal("", svc.HealthcheckTest);
    }

    [Fact]
    public void Validate_NamelessService_ShouldThrow()
    {
        var manifest = new DeploymentManifest
        {
            Nodes = { new NodeManifest { NodeId = "n1", Services = { new ManifestService { Name = "", Image = "img" } } } },
        };
        var ex = Assert.Throws<InvalidOperationException>(() => manifest.Validate());
        Assert.Contains("无名服务", ex.Message);
    }

    [Fact]
    public void Validate_DuplicateServiceName_ShouldThrow()
    {
        var manifest = new DeploymentManifest
        {
            Nodes =
            {
                new NodeManifest
                {
                    NodeId = "n1",
                    Services =
                    {
                        new ManifestService { Name = "a", Image = "img:a" },
                        new ManifestService { Name = "a", Image = "img:b" },
                    }
                }
            },
        };
        var ex = Assert.Throws<InvalidOperationException>(() => manifest.Validate());
        Assert.Contains("服务名重复", ex.Message);
    }

    [Fact]
    public void Validate_PortlessService_ShouldPass()
    {
        var manifest = new DeploymentManifest
        {
            Nodes = { new NodeManifest { NodeId = "n1", Services = { new ManifestService { Name = "a", Image = "img" } } } },
        };
        manifest.Validate(); // 不抛即通过：仅集群内访问的服务可以不占发布端口
    }

    [Fact]
    public void FromJson_NullJson_ShouldThrow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DeploymentManifest.FromJson("null"));
        Assert.Contains("反序列化为空", ex.Message);
    }
}

// ==================== PlacementPlanner 缺口 ====================

public class PlacementPlannerGapTests
{
    [Fact]
    public void MoveRole_BlankTarget_ShouldThrow()
    {
        var topo = new ClusterTopology
        {
            Nodes = { new ClusterNode { Id = "n1", Host = "10.0.0.1" } },
            Roles = { new ServiceRole { Id = "app" } },
        };
        Assert.Throws<ArgumentException>(() => PlacementPlanner.MoveRole(topo, "app", " "));
    }

    [Fact]
    public void RebalanceRole_NoNodes_ShouldThrow()
    {
        var topo = new ClusterTopology { Roles = { new ServiceRole { Id = "app" } } };
        var ex = Assert.Throws<InvalidOperationException>(() => PlacementPlanner.RebalanceRole(topo, "app"));
        Assert.Contains("机器清单为空", ex.Message);
    }
}

// ==================== SshBootstrapper 缺口 ====================

public class SshBootstrapperGapTests
{
    private sealed class FakeSsh : ISshTransport
    {
        /// <summary>按命令特征决定退出码；未命中默认 0。</summary>
        public Func<string, int> Responder { get; set; } = _ => 0;
        public List<string> Commands { get; } = new();

        public Task<(int ExitCode, List<string> Output)> RunAsync(string host, int port, string user, string credential, string command, CancellationToken ct)
        {
            Commands.Add(command);
            return Task.FromResult((Responder(command), new List<string> { "ok" }));
        }
    }

    private sealed class MemVault : ICredentialVault
    {
        private readonly Dictionary<string, string> _store = new();
        public void Store(string nodeId, string secret) => _store[nodeId] = secret;
        public string? Retrieve(string nodeId) => _store.TryGetValue(nodeId, out var v) ? v : null;
        public bool Delete(string nodeId) => _store.Remove(nodeId);
    }

    private static SshBootstrapper Create(FakeSsh ssh, Action<SshBootstrapOptions>? opt = null)
    {
        var options = new SshBootstrapOptions { AgentPackagePath = "/ctrl/agent.tar.gz" };
        opt?.Invoke(options);
        return new SshBootstrapper(ssh, options) { Vault = new MemVault() };
    }

    private static ClusterNode LinuxNode(string host = "10.0.0.1") => new()
    {
        Id = "n1", Name = "数据机", Os = NodeOs.Linux, Host = host, SshUser = "root",
    };

    [Fact]
    public async Task BootstrapLinux_EmptyHost_ShouldThrow()
    {
        var boot = Create(new FakeSsh());
        boot.Vault!.Store("n1", "pw");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(LinuxNode(host: ""), CancellationToken.None));
        Assert.Contains("机器地址为空", ex.Message);
    }

    [Fact]
    public async Task BootstrapLinux_BlankSshUser_ShouldThrow()
    {
        var boot = Create(new FakeSsh());
        boot.Vault!.Store("n1", "pw");
        var node = LinuxNode();
        node.SshUser = "  ";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("SSH 登录名为空", ex.Message);
    }

    [Fact]
    public async Task Constructor_DefaultOptions_DockerMissing_ShouldThrow_NoSetupScript()
    {
        // opt=null 走默认选项：未配容器安装脚本时，docker 缺失直接报错不半途装
        var ssh = new FakeSsh { Responder = cmd => cmd.Contains("command -v docker") ? 1 : 0 };
        var boot = new SshBootstrapper(ssh) { Vault = new MemVault() };
        boot.Vault!.Store("n1", "pw");
        var node = LinuxNode();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("未提供容器安装脚本", ex.Message);
        Assert.Equal(NodeState.Failed, node.State);
    }

    [Fact]
    public async Task BootstrapLinux_DockerMissing_SetupSucceeds_ShouldContinue()
    {
        var ssh = new FakeSsh { Responder = cmd => cmd.Contains("command -v docker") ? 1 : 0 };
        var boot = Create(ssh, opt => opt.ContainerSetupScript = "/ctrl/setup-docker.sh");
        boot.Vault!.Store("n1", "pw");
        var node = LinuxNode();

        await boot.BootstrapLinuxAsync(node, CancellationToken.None);

        Assert.Equal(NodeState.Online, node.State);
        Assert.Contains(ssh.Commands, c => c.Contains("setup-docker.sh")); // 容器安装脚本真实下发
    }

    [Fact]
    public async Task BootstrapLinux_SetupFails_ShouldThrow()
    {
        var ssh = new FakeSsh { Responder = cmd => cmd.Contains("setup-docker.sh") ? 5 : (cmd.Contains("command -v docker") ? 1 : 0) };
        var boot = Create(ssh, opt => opt.ContainerSetupScript = "/ctrl/setup-docker.sh");
        boot.Vault!.Store("n1", "pw");
        var node = LinuxNode();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("容器环境安装失败", ex.Message);
        Assert.Contains("exit=5", ex.Message);
        Assert.Equal(NodeState.Failed, node.State);
    }

    [Fact]
    public async Task BootstrapLinux_AgentInstallFails_ShouldThrow()
    {
        var ssh = new FakeSsh { Responder = cmd => cmd.Contains("install.sh") ? 3 : 0 };
        var boot = Create(ssh);
        boot.Vault!.Store("n1", "pw");
        var node = LinuxNode();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("代理安装失败", ex.Message);
        Assert.Equal(NodeState.Failed, node.State);
    }

    [Fact]
    public async Task BootstrapLinux_HealthConfirmFails_ShouldThrow()
    {
        var ssh = new FakeSsh { Responder = cmd => cmd.Contains("/health") ? 1 : 0 };
        var boot = Create(ssh);
        boot.Vault!.Store("n1", "pw");
        var node = LinuxNode();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("代理健康确认失败", ex.Message);
        Assert.Equal(NodeState.Failed, node.State);
    }
}

// ==================== UpgradeRollback 缺口 ====================

public class UpgradeRollbackGapTests
{
    private sealed class NoopExecutor : INodeExecutor
    {
        public Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct)
            => Task.FromResult(new AgentTaskResult { TaskId = task.TaskId, Success = true });
    }

    [Fact]
    public void RecordGood_BlankNodeId_ShouldThrow()
    {
        var ledger = new UpgradeLedger();
        Assert.Throws<ArgumentException>(() => ledger.RecordGood("", "{}"));
        Assert.Throws<ArgumentException>(() => ledger.RecordGood("  ", "{}"));
    }

    [Fact]
    public async Task Rollback_EmptyOrder_ShouldThrow()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ClusterRollback.RollbackAsync(new NoopExecutor(), new ClusterTopology(), new UpgradeLedger(),
                Array.Empty<string>(), "rb", new NodeProgressLedger(), CancellationToken.None));
        Assert.Contains("回滚顺序为空", ex.Message);
    }
}

// ==================== 分支收尾（100% 硬门禁）：空参抛错、failFast 取消等待、未知节点早退 ====================

public class BranchCloseOutGapTests
{
    [Fact]
    public void Orchestrator_NullExecutor_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new ClusterOrchestrator(null!));
    }

    private sealed class CancelOnFailExecutor : INodeExecutor
    {
        private readonly CancellationTokenSource _cts;
        public CancelOnFailExecutor(CancellationTokenSource cts) => _cts = cts;

        public async Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct)
        {
            if (node.Id == "n1")
            {
                _cts.Cancel(); // 先取消其余任务再报失败 → failFast 等待段走 OCE 兜底
                return new AgentTaskResult { TaskId = task.TaskId, Success = false, Error = "首机失败" };
            }
            try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
            return new AgentTaskResult { TaskId = task.TaskId, Success = false, Error = "被取消" };
        }
    }

    [Fact]
    public async Task InstallAll_FailFast_RemainingCancelled_ShouldStopWithoutUnwrap()
    {
        var cts = new CancellationTokenSource();
        var orch = new ClusterOrchestrator(new CancelOnFailExecutor(cts));
        var topo = new ClusterTopology();
        foreach (var id in new[] { "n1", "n2", "n3" })
            topo.Nodes.Add(new ClusterNode { Id = id, Host = "10.0.0.x", State = NodeState.Online });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orch.InstallAllAsync(topo, "t1", failFast: true, cts.Token));
        Assert.Contains("滚动停住", ex.Message);
        Assert.Contains("n1", ex.Message);
    }

    [Fact]
    public void SshBootstrapper_NullSsh_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new SshBootstrapper(null!));
    }

    [Fact]
    public void ApplyResult_UnknownNode_ShouldNotChangeRows()
    {
        var vm = new ClusterInstallViewModel();
        var topo = new ClusterTopology();
        topo.Nodes.Add(new ClusterNode { Id = "n1", Host = "10.0.0.1", State = NodeState.Online });
        topo.Nodes.Add(new ClusterNode { Id = "n2", Host = "10.0.0.2", State = NodeState.Online });
        vm.Load(topo);

        vm.ApplyResult(new AgentTaskResult { TaskId = "task:ghost", Success = false, Error = "幽灵节点" });

        Assert.All(vm.Rows, r => { Assert.False(r.Failed); Assert.Equal(0, r.Percent); });
        Assert.False(vm.AnyFailed); // 未知节点结果不入账，不误伤在装机器
    }
}
