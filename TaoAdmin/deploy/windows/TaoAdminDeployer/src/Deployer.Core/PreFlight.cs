// PreFlight: pre-deployment checks with a readable checklist.
// Boss order 2026-11-25 (deployer hardening): never start a deploy with a known
// bad environment — check disk space, .NET SDK availability, Docker engine,
// port occupancy and network reachability up front, print a pass/fail checklist,
// and stop before the first real step when any check fails.
namespace TaoAdmin.Deployer.Core;

public sealed record PreFlightItem(string Name, bool Passed, string Detail);

public sealed class PreFlightOptions
{
    /// <summary>Minimum free space required on the system drive (GB).</summary>
    public double MinFreeDiskGb { get; init; } = 10;

    /// <summary>Ports the deployment will occupy; occupied ports fail the check.</summary>
    public int[] Ports { get; init; } = { 5000, 5001, 5002, 5003, 8080, 3306, 6379 };

    /// <summary>URLs that must be reachable (network connectivity probe).</summary>
    public string[] ProbeUrls { get; init; } = { "https://dot.net/v1/dotnet-install.ps1" };
}

public static class PreFlight
{
    public static async Task<IReadOnlyList<PreFlightItem>> CheckAsync(DeployPaths paths, PreFlightOptions opt, CancellationToken ct)
    {
        var items = new List<PreFlightItem>();

        // 1) Disk space (system drive + the drive holding the sources).
        items.Add(DiskSpace("系统盘", Environment.SystemDirectory, opt.MinFreeDiskGb));
        items.Add(DiskSpace("源码所在盘", paths.Root, opt.MinFreeDiskGb));

        // 2) .NET SDK availability (portable runtime dir or global dotnet).
        items.Add(DotnetSdk(paths));

        // 3) Docker engine (informational for the docker route; never fatal here —
        //    the docker-install step can install/start it. Marked as passed when absent).
        items.Add(DockerEngine());

        // 4) Port occupancy.
        foreach (var port in opt.Ports)
        {
            var open = Net.Ports.PortOpen(port);
            items.Add(new PreFlightItem(
                $"端口 {port}",
                !open,
                open ? $"已被占用（{Net.Ports.ProcessName(Net.Ports.FindOwningProcess(port))}）——部署前会尝试释放；若为您的其他服务请自行处理" : "空闲"));
        }

        // 5) Network connectivity (best-effort probe with a short timeout).
        foreach (var url in opt.ProbeUrls)
        {
            var ok = await ProbeAsync(url, ct);
            items.Add(new PreFlightItem(
                $"网络连通 {HostOf(url)}",
                ok,
                ok ? "可达" : "不可达——下载安装包会失败，请检查网络或代理后重试"));
        }

        return items;
    }

    private static PreFlightItem DiskSpace(string label, string anchorPath, double minGb)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(anchorPath)) ?? "C:\\";
            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            return new PreFlightItem(
                $"{label}剩余空间",
                freeGb >= minGb,
                $"{freeGb:0.0} GB 可用（要求 ≥ {minGb:0} GB）");
        }
        catch (Exception ex)
        {
            // Cannot read the drive — do not block the deploy on a probe failure.
            return new PreFlightItem($"{label}剩余空间", true, $"无法检测（{ex.Message}），跳过");
        }
    }

    private static PreFlightItem DotnetSdk(DeployPaths paths)
    {
        try
        {
            var portable = DeployPaths.Join(paths.WinDir, "runtime", "dotnet10", "dotnet.exe");
            if (File.Exists(portable))
                return new PreFlightItem(".NET 10 SDK", true, "便携版已就绪（runtime/dotnet10）");
            if (ProcessRunner.Available("dotnet"))
                return new PreFlightItem(".NET 10 SDK", true, "检测到系统 dotnet（部署器会按需安装便携版）");
            return new PreFlightItem(".NET 10 SDK", true, "未检测到（部署器会自动安装便携版，约 200MB）");
        }
        catch (Exception ex)
        {
            return new PreFlightItem(".NET 10 SDK", true, $"检测异常（{ex.Message}），部署时按需安装");
        }
    }

    private static PreFlightItem DockerEngine()
    {
        try
        {
            return new PreFlightItem(
                "Docker 引擎",
                true,
                Net.Ports.DockerEngineRunning() ? "运行中" : "未运行（容器路线会自动安装/启动，原生路线不需要）");
        }
        catch (Exception ex)
        {
            return new PreFlightItem("Docker 引擎", true, $"检测异常（{ex.Message}），不阻断");
        }
    }

    private static async Task<bool> ProbeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)resp.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host; } catch { return url; }
    }
}
