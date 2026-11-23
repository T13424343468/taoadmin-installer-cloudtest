// ⑳分布式安装器：代理端（AgentService）。被控机上执行任务+状态上报。
// 职责：接收中控下发的任务（install/upgrade/start/stop/health），按四阶段
// （env→pull→start→health）执行本机安装，进度实时上报中控并落本地账本。
// 命令执行走 IAgentCommandRunner（真实实现 shell，测试可注入假通道）；上报走 IAgentReporter（真实实现 HTTP）。
using System.Diagnostics;
using System.Text.Json;

namespace TaoAdmin.Deployer.Core.Distributed;

/// <summary>代理本机命令执行通道抽象。</summary>
public interface IAgentCommandRunner
{
    /// <summary>执行本机命令，返回 (退出码, 输出行)。</summary>
    Task<(int ExitCode, List<string> Output)> RunAsync(string command, CancellationToken ct);
}

/// <summary>代理上报通道：进度/任务结果回传中控。</summary>
public interface IAgentReporter
{
    Task ReportProgressAsync(AgentProgress p, CancellationToken ct);
    Task ReportResultAsync(AgentTaskResult r, CancellationToken ct);
}

/// <summary>代理运行配置。</summary>
public sealed class AgentOptions
{
    /// <summary>本机节点 ID（与中控拓扑一致）。</summary>
    public string NodeId { get; set; } = "";

    /// <summary>代理监听端口（与 ClusterNode.AgentPort 一致）。</summary>
    public int Port { get; set; } = 5400;

    /// <summary>代理工作目录（compose 文件与安装产物落这里）。</summary>
    public string WorkDir { get; set; } = "/opt/taoadmin-agent";

    /// <summary>健康检查重试次数（含首次）。</summary>
    public int HealthRetries { get; set; } = 3;

    /// <summary>健康检查重试间隔（毫秒；测试可设 0）。</summary>
    public int HealthRetryDelayMs { get; set; } = 2000;
}

/// <summary>
/// 代理端服务：执行中控下发的任务并上报。
/// install=四阶段全跑；upgrade=pull→start→health；start=start→health；stop=单阶段停服；health=只做健康检查。
/// 任一阶段失败即停（不半途装成一半还报成功），失败原因带进结果回传中控。
/// </summary>
public sealed class AgentService
{
    public const string PhaseEnv = "env";
    public const string PhasePull = "pull";
    public const string PhaseStart = "start";
    public const string PhaseStop = "stop";
    public const string PhaseHealth = "health";

    /// <summary>安装四阶段固定顺序（与中控界面进度条口径一致）。</summary>
    public static readonly string[] InstallPhases = { PhaseEnv, PhasePull, PhaseStart, PhaseHealth };

    private readonly IAgentCommandRunner _runner;
    private readonly IAgentReporter _reporter;
    private readonly AgentOptions _opt;
    private readonly NodeProgressLedger _ledger = new();

    public AgentService(IAgentCommandRunner runner, IAgentReporter reporter, AgentOptions? opt = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _opt = opt ?? new AgentOptions();
        if (string.IsNullOrWhiteSpace(_opt.NodeId))
            throw new ArgumentException("代理 NodeId 不能为空", nameof(opt));
    }

    /// <summary>本地进度账本（界面/心跳兜底读取）。</summary>
    public NodeProgressLedger Ledger => _ledger;

    /// <summary>执行任务：按类型映射阶段序列，逐阶段执行并上报；结果统一回传中控。</summary>
    public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(task.TaskId))
            throw new ArgumentException("任务缺 TaskId", nameof(task));

        AgentTaskResult result;
        try
        {
            result = task.Kind switch
            {
                "install" => await RunPhasedAsync(task, InstallPhases, ct),
                "upgrade" => await RunPhasedAsync(task, new[] { PhasePull, PhaseStart, PhaseHealth }, ct),
                "start" => await RunPhasedAsync(task, new[] { PhaseStart, PhaseHealth }, ct),
                "stop" => await RunPhasedAsync(task, new[] { PhaseStop }, ct),
                "health" => await RunHealthOnlyAsync(task, ct),
                _ => throw new InvalidOperationException($"未知任务类型：{task.Kind}"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new AgentTaskResult { TaskId = task.TaskId, Success = false, Error = ex.Message };
        }

        await _reporter.ReportResultAsync(result, ct);
        return result;
    }

    /// <summary>心跳上报：中控据此判断代理存活（Phase=heartbeat，不占进度条）。</summary>
    public async Task SendHeartbeatAsync(CancellationToken ct = default)
    {
        var p = new AgentProgress { NodeId = _opt.NodeId, Phase = "heartbeat", Percent = 100, Message = "alive" };
        _ledger.Update(p);
        await _reporter.ReportProgressAsync(p, ct);
    }

    // ==================== 阶段执行 ====================

    private async Task<AgentTaskResult> RunPhasedAsync(AgentTask task, IReadOnlyList<string> phases, CancellationToken ct)
    {
        var result = new AgentTaskResult { TaskId = task.TaskId };
        var manifest = PrepareComposeFile(task);

        for (var i = 0; i < phases.Count; i++)
        {
            var phase = phases[i];
            var percent = (i + 1) * 100 / phases.Count;
            await ReportAsync(phase, percent, $"阶段 {phase} 开始");

            string? error;
            if (phase == PhaseHealth)
            {
                var (ok, healthError) = await HealthCheckAsync(manifest, ct);
                if (!ok) error = healthError;
                else error = null;
            }
            else
            {
                var command = CommandFor(phase, manifest);
                var run = await _runner.RunAsync(command, ct);
                if (run.ExitCode != 0)
                    error = $"阶段 {phase} 失败：exit={run.ExitCode}，输出：{string.Join(" | ", run.Output.TakeLast(3))}";
                else error = null;
            }

            if (error != null)
            {
                result.Success = false;
                result.Error = error;
                result.Output.Add(error);
                await ReportAsync(phase, percent, error);
                return result; // 坏阶段停住，后续阶段不跑
            }

            result.Output.Add($"[{phase}] ok");
            await ReportAsync(phase, percent, $"阶段 {phase} 完成");
        }

        result.Success = true;
        return result;
    }

    private async Task<AgentTaskResult> RunHealthOnlyAsync(AgentTask task, CancellationToken ct)
    {
        var result = new AgentTaskResult { TaskId = task.TaskId };
        var (ok, error) = await HealthCheckAsync(PrepareComposeFile(task), ct);
        result.Success = ok;
        if (!ok)
        {
            result.Error = error;      // HealthCheckAsync 契约：失败必带原因
            result.Output.Add(error!);
        }
        return result;
    }

    // ==================== 命令构造 ====================

    /// <summary>任务载荷带部署清单时，落盘 compose 文件（幂等覆盖，代理端不手改）。</summary>
    private NodeManifest? PrepareComposeFile(AgentTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Payload)) return null;
        NodeManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<NodeManifest>(task.Payload); }
        catch (JsonException ex) { throw new InvalidOperationException($"部署清单载荷解析失败：{ex.Message}"); }
        if (manifest is null) return null;

        Directory.CreateDirectory(_opt.WorkDir);
        var composePath = Path.Combine(_opt.WorkDir, "docker-compose.yml");
        File.WriteAllText(composePath, manifest.ToComposeYaml());
        return manifest;
    }

    private string ComposeFileOf(NodeManifest? manifest)
        => Path.Combine(_opt.WorkDir, "docker-compose.yml");

    internal string CommandFor(string phase, NodeManifest? manifest) => phase switch
    {
        PhaseEnv => "docker info", // 容器环境由 SSH 引导预装；代理只校验可用
        PhasePull => $"docker compose -f {ComposeFileOf(manifest)} pull",
        PhaseStart => $"docker compose -f {ComposeFileOf(manifest)} up -d",
        PhaseStop => $"docker compose -f {ComposeFileOf(manifest)} stop",
        _ => throw new InvalidOperationException($"未知阶段：{phase}"),
    };

    /// <summary>健康检查：curl 本机发布端口的服务健康路径，重试 N 次。</summary>
    private async Task<(bool Ok, string? Error)> HealthCheckAsync(NodeManifest? manifest, CancellationToken ct)
    {
        var target = HealthTargetOf(manifest);
        if (target is null)
            return (true, null); // 本机没有配健康路径的服务，视为通过（无服务可探）

        var (port, path) = target.Value;
        string? lastError = null;
        for (var attempt = 1; attempt <= Math.Max(1, _opt.HealthRetries); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var run = await _runner.RunAsync($"curl -fsS http://127.0.0.1:{port}{path}", ct);
            if (run.ExitCode == 0) return (true, null);
            lastError = $"健康检查第 {attempt}/{_opt.HealthRetries} 次失败：exit={run.ExitCode}";
            if (attempt < _opt.HealthRetries && _opt.HealthRetryDelayMs > 0)
                await Task.Delay(_opt.HealthRetryDelayMs, ct);
        }
        return (false, lastError);
    }

    private static (int Port, string Path)? HealthTargetOf(NodeManifest? manifest)
    {
        var svc = manifest?.Services.FirstOrDefault(s => s.HealthPath is not null && s.HostPort is not null);
        return svc is null ? null : (svc.HostPort!.Value, svc.HealthPath!);
    }

    private async Task ReportAsync(string phase, int percent, string message)
    {
        var p = new AgentProgress { NodeId = _opt.NodeId, Phase = phase, Percent = percent, Message = message };
        _ledger.Update(p);
        await _reporter.ReportProgressAsync(p, CancellationToken.None); // 进度上报不因取消令牌中断，确保中控可见
    }
}
