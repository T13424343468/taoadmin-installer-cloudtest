// ⑳分布式安装器：SSH 引导。首次连接自动装容器环境和代理，之后走代理通道。
// Linux：填 IP+SSH 账号密码（或密钥）即可；Windows：优先试系统可选 SSH，不通提示手动装代理包。
namespace TaoAdmin.Deployer.Core.Distributed;

public interface ISshTransport
{
    /// <summary>执行远程命令，返回 (exitCode, outputLines)。</summary>
    Task<(int ExitCode, List<string> Output)> RunAsync(string host, int port, string user, string credential, string command, CancellationToken ct);
}

public sealed class SshBootstrapOptions
{
    /// <summary>代理安装包在中控侧的路径（引导时上传到被控机）。</summary>
    public string AgentPackagePath { get; set; } = "";

    /// <summary>容器环境安装脚本（Linux：docker 安装；Windows：Docker Desktop 静默装）。</summary>
    public string ContainerSetupScript { get; set; } = "";

    /// <summary>引导超时（秒）。</summary>
    public int TimeoutSeconds { get; set; } = 600;
}

public sealed class SshBootstrapper
{
    private readonly ISshTransport _ssh;
    private readonly SshBootstrapOptions _opt;

    public SshBootstrapper(ISshTransport ssh, SshBootstrapOptions? opt = null)
    {
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
        _opt = opt ?? new SshBootstrapOptions();
    }

    /// <summary>
    /// 引导一台 Linux 机器：连通性探测 → 装容器环境 → 装代理 → 启动代理 → 健康确认。
    /// 任一步失败抛 InvalidOperationException，节点置 Failed，不半途装成一半还不报错。
    /// </summary>
    public async Task BootstrapLinuxAsync(ClusterNode node, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Os != NodeOs.Linux) throw new InvalidOperationException("本入口只接 Linux 机器");
        if (string.IsNullOrWhiteSpace(node.Host)) throw new InvalidOperationException("机器地址为空");
        if (string.IsNullOrWhiteSpace(node.SshUser)) throw new InvalidOperationException("SSH 登录名为空");

        node.State = NodeState.Bootstrapping;
        try
        {
            var probe = await _ssh.RunAsync(node.Host, node.SshPort, node.SshUser, CredentialOf(node), "echo ok", ct);
            if (probe.ExitCode != 0 || !probe.Output.Any(l => l.Trim() == "ok"))
                throw new InvalidOperationException($"SSH 连通失败：{node.Host}:{node.SshPort}");

            var docker = await _ssh.RunAsync(node.Host, node.SshPort, node.SshUser, CredentialOf(node), "command -v docker", ct);
            if (docker.ExitCode != 0)
            {
                if (string.IsNullOrWhiteSpace(_opt.ContainerSetupScript))
                    throw new InvalidOperationException("被控机无 docker 且未提供容器安装脚本");
                var setup = await _ssh.RunAsync(node.Host, node.SshPort, node.SshUser, CredentialOf(node),
                    $"bash -s < {_opt.ContainerSetupScript}", ct);
                if (setup.ExitCode != 0) throw new InvalidOperationException($"容器环境安装失败：exit={setup.ExitCode}");
            }

            if (string.IsNullOrWhiteSpace(_opt.AgentPackagePath))
                throw new InvalidOperationException("未提供代理安装包路径");
            var agent = await _ssh.RunAsync(node.Host, node.SshPort, node.SshUser, CredentialOf(node),
                $"mkdir -p /opt/taoadmin-agent && tar -xzf - -C /opt/taoadmin-agent < {_opt.AgentPackagePath} && /opt/taoadmin-agent/install.sh --port {node.AgentPort}", ct);
            if (agent.ExitCode != 0) throw new InvalidOperationException($"代理安装失败：exit={agent.ExitCode}");

            var health = await _ssh.RunAsync(node.Host, node.SshPort, node.SshUser, CredentialOf(node),
                $"curl -fsS http://127.0.0.1:{node.AgentPort}/health", ct);
            if (health.ExitCode != 0) throw new InvalidOperationException("代理健康确认失败");

            node.State = NodeState.Online;
            node.LastError = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            node.State = NodeState.Failed;
            node.LastError = ex.Message;
            throw;
        }
    }

    /// <summary>Windows 机器接入：先试系统可选 SSH，不通则返回 false 提示手动装代理包。</summary>
    public async Task<bool> TryBootstrapWindowsAsync(ClusterNode node, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Os != NodeOs.Windows) throw new InvalidOperationException("本入口只接 Windows 机器");
        var probe = await _ssh.RunAsync(node.Host, node.SshPort, node.SshUser, CredentialOf(node), "echo ok", ct);
        if (probe.ExitCode != 0 || !probe.Output.Any(l => l.Trim() == "ok"))
        {
            node.State = NodeState.Pending;
            node.LastError = "Windows 机器 SSH 不可达：请开启系统可选 SSH 功能，或手动安装代理包";
            return false;
        }
        node.State = NodeState.Online;
        node.LastError = null;
        return true;
    }

    private string CredentialOf(ClusterNode node) => Vault?.Retrieve(node.Id)
        ?? throw new InvalidOperationException($"节点 {node.Id} 凭据未托管，拒绝明文传参");

    /// <summary>凭据库由中控注入；引导过程只从库里取，不收明文。</summary>
    public ICredentialVault? Vault { get; set; }
}
