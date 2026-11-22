// Net helpers: port probing, HTTP health checks, docker engine detection.
namespace TaoAdmin.Deployer.Core.Net;

public static class Ports
{
    public static bool PortOpen(int port)
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            // IPAddress.Loopback: skip DNS resolution entirely; generous window for busy machines.
            var t = c.ConnectAsync(System.Net.IPAddress.Loopback, port);
            if (!t.Wait(2000)) return false;
            c.Close();
            return true;
        }
        catch { return false; }
    }

    public static async Task<bool> WaitPortOpenAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (PortOpen(port)) return true;
            await Task.Delay(2000, ct);
        }
        return PortOpen(port);
    }

    // Waits until none of the ports is connectable anymore (processes fully exited).
    public static async Task WaitPortsClosedAsync(int[] ports, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            bool anyOpen = false;
            foreach (var p in ports)
            {
                ct.ThrowIfCancellationRequested();
                if (PortOpen(p)) { anyOpen = true; break; }
            }
            if (!anyOpen) return;
            await Task.Delay(1000, ct);
        }
    }

    public static bool DockerEngineRunning()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task<bool> WaitDockerEngineAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (DockerEngineRunning()) return true;
            await Task.Delay(5000, ct);
        }
        return DockerEngineRunning();
    }

    public static int FindOwningProcess(int port)
    {
        // Windows-only; requires System.Diagnostics.Process with performance counters.
        // Uses `netstat -ano` for reliable cross-version behavior.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return -1;
            while (!p.StandardOutput.EndOfStream)
            {
                var line = p.StandardOutput.ReadLine() ?? "";
                // Format: TCP    127.0.0.1:5000    0.0.0.0:0    LISTENING    1234
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[0] == "TCP" && parts[3] == "LISTENING")
                {
                    var local = parts[1];
                    var idx = local.LastIndexOf(':');
                    if (idx > 0 && int.TryParse(local[(idx + 1)..], out var lp) && lp == port &&
                        int.TryParse(parts[4], out var pid))
                        return pid;
                }
            }
            p.WaitForExit(5000);
        }
        catch { }
        return -1;
    }

    public static string ProcessName(int pid)
    {
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch { return "unknown"; }
    }

    public static async Task EnsurePortFreeAsync(int port, Func<PortConflictInfo, bool>? confirm, Action<string>? log, CancellationToken ct)
    {
        if (!PortOpen(port)) return;
        var pid = FindOwningProcess(port);
        if (pid <= 0 || pid == Environment.ProcessId) return;
        var pname = ProcessName(pid);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dotnet", "mysqld", "redis-server", "node",
            "TaoAdmin.GatewayService", "TaoAdmin.IdentityService", "TaoAdmin.TaskService", "TaoFiles.Api",
        };
        var stop = known.Contains(pname) || (confirm?.Invoke(new PortConflictInfo(port, pid, pname)) ?? false);
        if (!stop)
            throw new InvalidOperationException($"端口 {port} 被进程 {pname}（PID {pid}）占用，且未获授权停止它。请关闭该程序后重试。");
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            log?.Invoke($"已停止占用端口 {port} 的进程 {pname}（PID {pid}）");
            await Task.Delay(2000, ct);
        }
        catch { }
    }
}

public static class Http
{
    public static async Task<bool> WaitOkAsync(string url, TimeSpan timeout, CancellationToken ct, Action<int>? onWaiting = null)
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var deadline = DateTime.UtcNow + timeout;
        var started = DateTime.UtcNow;
        var lastReport = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var resp = await http.GetAsync(url, ct);
                if ((int)resp.StatusCode < 500) return true;
            }
            catch { }
            // 等服务期间每 10 秒报一次进度，用户知道程序没卡死
            if (onWaiting != null && (DateTime.UtcNow - lastReport).TotalSeconds >= 10)
            {
                onWaiting((int)(DateTime.UtcNow - started).TotalSeconds);
                lastReport = DateTime.UtcNow;
            }
            await Task.Delay(3000, ct);
        }
        return false;
    }
}
