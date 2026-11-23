using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

/// <summary>
/// 中控侧代理通道与安装视图模型实效断言：
/// 代理不可达转失败结果不抛出、进度轮询静默跳过、
/// 四阶段只前进不回退、失败置红、整体进度均值、TaskId 反解节点。
/// </summary>
public class ClusterInstallTests
{
    private sealed class FakeHttp : IAgentHttpClient
    {
        public Func<string, AgentTask, AgentTaskResult> TaskResponder { get; set; } =
            (_, t) => new AgentTaskResult { TaskId = t.TaskId, Success = true };
        public AgentProgress? NextProgress { get; set; }
        public bool ThrowOnPost { get; set; }
        public bool ThrowOnPoll { get; set; }
        public List<string> Urls { get; } = new();

        public Task<AgentTaskResult> PostTaskAsync(string baseUrl, AgentTask task, CancellationToken ct)
        {
            Urls.Add(baseUrl);
            if (ThrowOnPost) throw new HttpRequestException("connection refused");
            return Task.FromResult(TaskResponder(baseUrl, task));
        }

        public Task<AgentProgress?> GetProgressAsync(string baseUrl, string nodeId, CancellationToken ct)
        {
            if (ThrowOnPoll) throw new HttpRequestException("connection refused");
            return Task.FromResult(NextProgress);
        }
    }

    private static ClusterNode Node(string id, string host = "10.0.0.1") => new()
    { Id = id, Name = id, Host = host, AgentPort = 5400, State = NodeState.Online };

    // ==================== AgentNodeExecutor ====================

    [Fact]
    public async Task Executor_ShouldPostToAgentEndpoint()
    {
        var http = new FakeHttp();
        var executor = new AgentNodeExecutor(http);
        var node = Node("n1", "10.0.0.9");
        var ledger = new NodeProgressLedger();

        var result = await executor.ExecuteAsync(node, new AgentTask { TaskId = "t:n1", Kind = "install" }, ledger, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("http://10.0.0.9:5400", http.Urls.Single());
    }

    [Fact]
    public async Task Executor_AgentUnreachable_ShouldReturnFailure_NotThrow()
    {
        var executor = new AgentNodeExecutor(new FakeHttp { ThrowOnPost = true });
        var node = Node("n1");
        var result = await executor.ExecuteAsync(node, new AgentTask { TaskId = "t:n1", Kind = "install" }, new NodeProgressLedger(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("代理通道不可达", result.Error);
        Assert.Contains("10.0.0.1:5400", result.Error);
    }

    [Fact]
    public async Task PollProgress_ShouldUpdateLedger_AndSkipUnreachable()
    {
        var http = new FakeHttp { NextProgress = new AgentProgress { NodeId = "n1", Phase = "pull", Percent = 50, Message = "拉镜像中" } };
        var executor = new AgentNodeExecutor(http);
        var ledger = new NodeProgressLedger();

        await executor.PollProgressAsync(Node("n1"), ledger, CancellationToken.None);
        Assert.Equal(50, ledger.Get("n1")?.Percent);

        // 代理不可达：静默跳过，账本保留旧值
        http.ThrowOnPoll = true;
        await executor.PollProgressAsync(Node("n1"), ledger, CancellationToken.None);
        Assert.Equal(50, ledger.Get("n1")?.Percent);
    }

    // ==================== ClusterInstallViewModel ====================

    private static ClusterTopology TwoNodeTopo() => new()
    {
        Nodes = new List<ClusterNode> { Node("n1", "10.0.0.1"), Node("n2", "10.0.0.2") },
        Roles = new List<ServiceRole> { new() { Id = "app", Name = "应用" } },
        Placements = new List<Placement> { new() { RoleId = "app", InstanceIndex = 0, NodeId = "n1" } },
    };

    [Fact]
    public void Load_ShouldCreateRowPerNode()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());

        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal("待开始", r.PhaseText));
        Assert.Equal(0, vm.OverallPercent);
        Assert.False(vm.AnyFailed);
    }

    [Fact]
    public void ApplyProgress_ShouldAdvancePhases_NotRegress()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());

        vm.ApplyProgress(new AgentProgress { NodeId = "n1", Phase = "pull", Percent = 50, Message = "拉镜像" });
        var row = vm.Rows.First(r => r.NodeId == "n1");
        Assert.Equal("拉镜像", row.PhaseText);
        Assert.Equal(50, row.Percent);

        // 迟到的旧进度（env 阶段）不回退
        vm.ApplyProgress(new AgentProgress { NodeId = "n1", Phase = "env", Percent = 25, Message = "装环境" });
        Assert.Equal("拉镜像", row.PhaseText);
        Assert.Equal(50, row.Percent);
    }

    [Fact]
    public void ApplyProgress_Heartbeat_ShouldNotOccupyProgress()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());
        vm.ApplyProgress(new AgentProgress { NodeId = "n1", Phase = "heartbeat", Percent = 100, Message = "alive" });

        Assert.Equal("待开始", vm.Rows[0].PhaseText);
        Assert.Equal(0, vm.OverallPercent);
    }

    [Fact]
    public void ApplyResult_Failure_ShouldMarkRowFailed()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());
        vm.ApplyResult(new AgentTaskResult { TaskId = "task:n2", Success = false, Error = "拉镜像超时" });

        var row = vm.Rows.First(r => r.NodeId == "n2");
        Assert.True(row.Failed);
        Assert.Contains("拉镜像超时", row.Message);
        Assert.True(vm.AnyFailed);

        vm.ResetFailures();
        Assert.False(vm.AnyFailed);
    }

    [Fact]
    public void ApplyResult_Success_ShouldCompleteRow()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());
        vm.ApplyResult(new AgentTaskResult { TaskId = "task:n1", Success = true });

        var row = vm.Rows.First(r => r.NodeId == "n1");
        Assert.Equal(100, row.Percent);
        Assert.Equal("安装完成", row.Message);
        Assert.Equal(50, vm.OverallPercent); // n1 满、n2 未动 → 均值 50
    }

    [Fact]
    public void Changed_ShouldFireOnUpdate()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());
        var fired = 0;
        vm.Changed += () => fired++;

        vm.ApplyProgress(new AgentProgress { NodeId = "n1", Phase = "env", Percent = 25, Message = "" });
        Assert.Equal(1, fired);
    }

    [Fact]
    public void ApplyProgress_UnknownNode_ShouldBeIgnored()
    {
        var vm = new ClusterInstallViewModel();
        vm.Load(TwoNodeTopo());
        vm.ApplyProgress(new AgentProgress { NodeId = "ghost", Phase = "pull", Percent = 50, Message = "" });
        Assert.Equal(0, vm.OverallPercent);
    }
}
