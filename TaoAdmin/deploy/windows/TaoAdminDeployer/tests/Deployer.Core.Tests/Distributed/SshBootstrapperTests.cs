using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class SshBootstrapperTests
{
    private sealed class FakeSsh : ISshTransport
    {
        public Func<string, (int, List<string>)> Responder { get; set; } = _ => (0, new List<string> { "ok" });
        public List<string> Commands { get; } = new();

        public Task<(int ExitCode, List<string> Output)> RunAsync(string host, int port, string user, string credential, string command, CancellationToken ct)
        {
            Commands.Add(command);
            var (code, lines) = Responder(command);
            return Task.FromResult((code, lines));
        }
    }

    private sealed class MemVault : ICredentialVault
    {
        private readonly Dictionary<string, string> _store = new();
        public void Store(string nodeId, string secret) => _store[nodeId] = secret;
        public string? Retrieve(string nodeId) => _store.TryGetValue(nodeId, out var v) ? v : null;
        public bool Delete(string nodeId) => _store.Remove(nodeId);
    }

    private static ClusterNode LinuxNode() => new()
    {
        Id = "n1", Name = "数据机", Os = NodeOs.Linux, Host = "10.0.0.1", SshUser = "root",
    };

    [Fact]
    public async Task BootstrapLinux_HappyPath_Online()
    {
        var ssh = new FakeSsh();
        var boot = new SshBootstrapper(ssh, new SshBootstrapOptions
        {
            ContainerSetupScript = "/ctrl/setup-docker.sh",
            AgentPackagePath = "/ctrl/agent.tar.gz",
        }) { Vault = new MemVault() };
        var node = LinuxNode();
        boot.Vault!.Store("n1", "pw");

        await boot.BootstrapLinuxAsync(node, CancellationToken.None);

        Assert.Equal(NodeState.Online, node.State);
        Assert.Null(node.LastError);
        Assert.Contains(ssh.Commands, c => c.Contains("command -v docker"));
        Assert.Contains(ssh.Commands, c => c.Contains("install.sh --port 5400"));
        Assert.Contains(ssh.Commands, c => c.Contains("/health"));
    }

    [Fact]
    public async Task BootstrapLinux_SshFail_MarksFailed()
    {
        var ssh = new FakeSsh { Responder = _ => (255, new List<string>()) };
        var boot = new SshBootstrapper(ssh) { Vault = new MemVault() };
        var node = LinuxNode();
        boot.Vault!.Store("n1", "pw");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Equal(NodeState.Failed, node.State);
        Assert.Contains("SSH 连通失败", node.LastError);
        Assert.Contains("SSH 连通失败", ex.Message);
    }

    [Fact]
    public async Task BootstrapLinux_NoDockerNoScript_Throws()
    {
        var ssh = new FakeSsh
        {
            Responder = cmd => cmd.Contains("command -v docker") ? (1, new List<string>()) : (0, new List<string> { "ok" }),
        };
        var boot = new SshBootstrapper(ssh, new SshBootstrapOptions { AgentPackagePath = "/ctrl/agent.tar.gz" }) { Vault = new MemVault() };
        var node = LinuxNode();
        boot.Vault!.Store("n1", "pw");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("未提供容器安装脚本", ex.Message);
        Assert.Equal(NodeState.Failed, node.State);
    }

    [Fact]
    public async Task BootstrapLinux_NoAgentPackage_Throws()
    {
        var ssh = new FakeSsh(); // docker 已装（echo ok 且 command -v docker 返回 0）
        var boot = new SshBootstrapper(ssh, new SshBootstrapOptions()) { Vault = new MemVault() };
        var node = LinuxNode();
        boot.Vault!.Store("n1", "pw");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("代理安装包", ex.Message);
    }

    [Fact]
    public async Task BootstrapLinux_NoVault_ThrowsWithoutPlaintext()
    {
        var ssh = new FakeSsh();
        var boot = new SshBootstrapper(ssh, new SshBootstrapOptions { AgentPackagePath = "/x" }); // Vault 未注入
        var node = LinuxNode();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => boot.BootstrapLinuxAsync(node, CancellationToken.None));
        Assert.Contains("凭据未托管", ex.Message);
        Assert.Equal(NodeState.Failed, node.State);
    }

    [Fact]
    public async Task TryBootstrapWindows_SshOk_Online_SshFail_Pending()
    {
        var sshOk = new FakeSsh();
        var bootOk = new SshBootstrapper(sshOk) { Vault = new MemVault() };
        bootOk.Vault!.Store("n1", "pw");
        var node = new ClusterNode { Id = "n1", Os = NodeOs.Windows, Host = "10.0.0.9" };
        Assert.True(await bootOk.TryBootstrapWindowsAsync(node, CancellationToken.None));
        Assert.Equal(NodeState.Online, node.State);

        var sshFail = new FakeSsh { Responder = _ => (255, new List<string>()) };
        var bootFail = new SshBootstrapper(sshFail) { Vault = new MemVault() };
        bootFail.Vault!.Store("n1", "pw");
        var node2 = new ClusterNode { Id = "n1", Os = NodeOs.Windows, Host = "10.0.0.9" };
        Assert.False(await bootFail.TryBootstrapWindowsAsync(node2, CancellationToken.None));
        Assert.Equal(NodeState.Pending, node2.State);
        Assert.Contains("手动安装代理包", node2.LastError);
    }

    [Fact]
    public async Task Bootstrap_WrongOs_Throws()
    {
        var boot = new SshBootstrapper(new FakeSsh());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.BootstrapLinuxAsync(new ClusterNode { Id = "w1", Os = NodeOs.Windows, Host = "x" }, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            boot.TryBootstrapWindowsAsync(new ClusterNode { Id = "l1", Os = NodeOs.Linux, Host = "x" }, CancellationToken.None));
    }
}
