// ⑳分布式安装器：中控侧代理通道与安装视图模型。
// AgentNodeExecutor：把任务经 HTTP 下发到被控机代理（POST /tasks），并轮询进度入账。
// ClusterInstallViewModel：中控界面的安装进度状态（机器×四阶段），纯逻辑可测，界面只做绑定渲染。
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>代理 HTTP 通道抽象（真实实现 HttpClient，测试可注入假通道）。</summary>
public interface IAgentHttpClient
{
    Task<AgentTaskResult> PostTaskAsync(string baseUrl, AgentTask task, CancellationToken ct);
    Task<AgentProgress?> GetProgressAsync(string baseUrl, string nodeId, CancellationToken ct);
}

/// <summary>默认实现：代理监听 http://{host}:{agentPort}，接口口径 POST /tasks、GET /progress?nodeId=。</summary>
public sealed class AgentHttpClient : IAgentHttpClient
{
    private readonly HttpClient _http;

    public AgentHttpClient(HttpClient? http = null) => _http = http ?? new HttpClient();

    public async Task<AgentTaskResult> PostTaskAsync(string baseUrl, AgentTask task, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/tasks", task, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<AgentTaskResult>(cancellationToken: ct)
               ?? throw new InvalidOperationException("代理返回任务结果为空");
    }

    public async Task<AgentProgress?> GetProgressAsync(string baseUrl, string nodeId, CancellationToken ct)
    {
        var resp = await _http.GetAsync($"{baseUrl.TrimEnd('/')}/progress?nodeId={Uri.EscapeDataString(nodeId)}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<AgentProgress>(cancellationToken: ct);
    }
}

/// <summary>代理执行通道：中控把任务下发到被控机代理，失败转失败结果（不抛出，坏机停住口径统一在编排层）。</summary>
public sealed class AgentNodeExecutor : INodeExecutor
{
    private readonly IAgentHttpClient _http;

    public AgentNodeExecutor(IAgentHttpClient? http = null) => _http = http ?? new AgentHttpClient();

    public async Task<AgentTaskResult> ExecuteAsync(ClusterNode node, AgentTask task, NodeProgressLedger ledger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(ledger);
        var baseUrl = $"http://{node.Host}:{node.AgentPort}";
        try
        {
            var result = await _http.PostTaskAsync(baseUrl, task, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AgentTaskResult { TaskId = task.TaskId, Success = false, Error = $"代理通道不可达（{baseUrl}）：{ex.Message}" };
        }
    }

    /// <summary>轮询一台机器的代理进度并入账（界面定时器调用；代理不可达静默跳过，不打断界面）。</summary>
    public async Task PollProgressAsync(ClusterNode node, NodeProgressLedger ledger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(ledger);
        try
        {
            var p = await _http.GetProgressAsync($"http://{node.Host}:{node.AgentPort}", node.Id, ct);
            if (p is not null) ledger.Update(p);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch { /* 代理暂时不可达：进度轮询不打断安装 */ }
    }
}

/// <summary>中控界面单机进度行（机器×四阶段）。</summary>
public sealed class NodeProgressRow
{
    public string NodeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public NodeState State { get; set; } = NodeState.Pending;
    /// <summary>当前阶段：env/pull/start/health/heartbeat/空（未开始）。</summary>
    public string Phase { get; set; } = "";
    public int Percent { get; set; }
    public string Message { get; set; } = "";
    public bool Failed { get; set; }

    /// <summary>阶段中文名（与代理四阶段口径一致）。</summary>
    public string PhaseText => Phase switch
    {
        AgentService.PhaseEnv => "装环境",
        AgentService.PhasePull => "拉镜像",
        AgentService.PhaseStart => "起服务",
        AgentService.PhaseHealth => "健康检查",
        AgentService.PhaseStop => "停服",
        "heartbeat" => "在线",
        "" => "待开始",
        _ => Phase,
    };
}

/// <summary>
/// 并行安装视图模型：机器清单 + 每机四阶段进度 + 整体进度。
/// 数据源：进度账本（AgentNodeExecutor 轮询入账）+ 任务结果；界面只订阅 Changed 渲染。
/// </summary>
public sealed class ClusterInstallViewModel
{
    /// <summary>四阶段显示顺序（进度条口径）。</summary>
    public static readonly string[] DisplayPhases = { AgentService.PhaseEnv, AgentService.PhasePull, AgentService.PhaseStart, AgentService.PhaseHealth };

    public List<NodeProgressRow> Rows { get; } = new();

    /// <summary>状态变化通知（界面线程自行调度渲染）。</summary>
    public event Action? Changed;

    /// <summary>整体进度 = 各机进度的平均值（未开始按 0 计）。</summary>
    public int OverallPercent => Rows.Count == 0 ? 0 : (int)Math.Round(Rows.Average(r => r.Percent));

    /// <summary>是否有失败机器（坏机停住口径：界面置红提示）。</summary>
    public bool AnyFailed => Rows.Any(r => r.Failed);

    public void Load(ClusterTopology topo)
    {
        ArgumentNullException.ThrowIfNull(topo);
        Rows.Clear();
        foreach (var n in topo.Nodes)
            Rows.Add(new NodeProgressRow { NodeId = n.Id, Name = n.Name, Host = n.Host, State = n.State });
        EmitChanged();
    }

    /// <summary>进度入账：更新对应机器行（阶段只前进不回退；失败标记保留）。</summary>
    public void ApplyProgress(AgentProgress p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Phase == "heartbeat") return; // 心跳不占进度条
        var row = Rows.FirstOrDefault(r => r.NodeId == p.NodeId);
        if (row is null) return;
        if (PercentRank(p.Phase) >= PercentRank(row.Phase))
        {
            row.Phase = p.Phase;
            row.Percent = Math.Max(row.Percent, p.Percent);
        }
        row.Message = p.Message;
        EmitChanged();
    }

    /// <summary>任务结果入账：失败置红，成功把该机进度拉满。</summary>
    public void ApplyResult(AgentTaskResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var nodeId = r.TaskId.Contains(':') ? r.TaskId[(r.TaskId.IndexOf(':') + 1)..] : r.TaskId;
        var row = Rows.FirstOrDefault(x => x.NodeId == nodeId);
        if (row is null) return;
        if (r.Success)
        {
            row.Percent = 100;
            row.Phase = AgentService.PhaseHealth;
            row.Message = "安装完成";
        }
        else
        {
            row.Failed = true;
            row.Message = r.Error ?? "安装失败";
        }
        EmitChanged();
    }

    /// <summary>重置失败标记（重试前调用）。</summary>
    public void ResetFailures()
    {
        foreach (var r in Rows) { r.Failed = false; }
        EmitChanged();
    }

    private static int PercentRank(string phase) => phase switch
    {
        AgentService.PhaseEnv => 0,
        AgentService.PhasePull => 1,
        AgentService.PhaseStart => 2,
        AgentService.PhaseHealth => 3,
        _ => -1,
    };

    private void EmitChanged() => Changed?.Invoke();
}
