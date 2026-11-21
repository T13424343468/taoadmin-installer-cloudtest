// DeployPaths: central path computation for the deployer.
// Windows-style paths are parsed manually (both separators) so logic is testable on Linux.
namespace TaoAdmin.Deployer.Core;

public sealed class DeployPaths
{
    public string Root { get; }        // TaoAdmin root (two levels above deploy/windows)
    public string WinDir { get; }      // deploy/windows
    public string RuntimeDir { get; }
    public string PidDir { get; }
    public string LogDir { get; }
    public string DlDir { get; }
    public string PublishDir { get; }
    public string StateFile { get; }
    public string SqlFile { get; }
    public string ComposeDir { get; }
    public string FrontendDir { get; }
    public string RebootMarker { get; }

    public DeployPaths(string winDir)
    {
        WinDir = NormalizeDir(winDir);
        Root = NormalizeDir(ParentDir(ParentDir(WinDir)));
        RuntimeDir = Join(WinDir, "runtime");
        PidDir = Join(RuntimeDir, "pids");
        LogDir = Join(RuntimeDir, "logs");
        DlDir = Join(RuntimeDir, "downloads");
        PublishDir = Join(Root, "deploy", "publish");
        StateFile = Join(RuntimeDir, ".install-state");
        SqlFile = Join(Root, "deploy", "sql", "tao_admin_init.sql");
        ComposeDir = Join(Root, "deploy");
        FrontendDir = Join(Root, "frontend");
        RebootMarker = Join(RuntimeDir, ".reboot-pending");
    }

    /// <summary>Locate the deploy/windows directory from the executable location.
    /// Layout: &lt;root&gt;/deploy/windows/TaoAdminDeployer.exe (single file) or in a subfolder.</summary>
    public static DeployPaths FromExecutable(string exePath)
    {
        var dir = NormalizeDir(Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? ".");
        // Walk up until a directory named "windows" whose parent is "deploy" is found (max 6 levels).
        var cur = dir;
        for (var i = 0; i < 6; i++)
        {
            var name = LeafName(cur);
            var parent = ParentDir(cur);
            if (string.Equals(name, "windows", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(LeafName(parent), "deploy", StringComparison.OrdinalIgnoreCase))
            {
                return new DeployPaths(cur);
            }
            var next = parent;
            if (string.Equals(next, cur, StringComparison.OrdinalIgnoreCase)) break;
            cur = next;
        }
        // Fallback: assume exe sits directly in deploy/windows.
        return new DeployPaths(dir);
    }

    // ---- manual path helpers (separator-agnostic, pitfall #11) ----

    public static string NormalizeDir(string path)
    {
        var p = path.TrimEnd('/', '\\');
        return p.Length == 0 ? path : p;
    }

    public static string ParentDir(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var idx = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        if (idx <= 0)
        {
            // Root like "C:\" or "/" — return the root itself.
            return idx < 0 ? trimmed : trimmed.Substring(0, idx + 1).TrimEnd('/', '\\');
        }
        return trimmed.Substring(0, idx);
    }

    public static string LeafName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var idx = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        return idx < 0 ? trimmed : trimmed.Substring(idx + 1);
    }

    public static string Join(string basePath, params string[] parts)
    {
        var cur = basePath.TrimEnd('/', '\\');
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;
            cur = cur + Path.DirectorySeparatorChar + part;
        }
        return cur;
    }
}
