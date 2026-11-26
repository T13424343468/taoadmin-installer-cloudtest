// HeadlessRunner: CLI entry for CI and power users.
//   TaoAdminDeployer.exe --deploy --route native --yes
//   TaoAdminDeployer.exe --deploy --route docker --yes
//   TaoAdminDeployer.exe --uninstall --yes [--keep-runtimes]
//   TaoAdminDeployer.exe --status
namespace TaoAdmin.Deployer.App;

using System.IO;
using TaoAdmin.Deployer.Core;

public static class HeadlessRunner
{
    public static int ExitCode { get; private set; }

    public static void Run(string[] args)
    {
        // WinExe has no console; attach to the parent console so CI captures output.
        TryAttachConsole();
        // Rebind stdout explicitly: AttachConsole can leave the handle stale, and CI
        // redirection must never depend on console attachment. Everything is also
        // teed to deployer-headless.log so a run is always diagnosable.
        try { Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }); } catch { }
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        using var fileLog = OpenFileLog();
        fileLogStatic = fileLog;

        void Tee(string line)
        {
            Console.WriteLine(line);
            fileLog?.WriteLine(line);
        }

        try
        {
            var paths = DeployPaths.FromExecutable(Environment.ProcessPath ?? AppContext.BaseDirectory);
            var engine = new DeployEngine(paths, BuildOptions(args));
            engine.Log = l => Tee("[LOG] " + l);
            engine.StepOutput = (_, line) => Tee("      " + line);

            if (args.Contains("--status"))
            {
                var s = engine.Status();
                foreach (var i in s.Items)
                    Tee($"  {i.Name,-16} {(i.Running ? "运行中" : "未运行")}");
                foreach (var c in s.Containers)
                    Tee("  容器: " + c);
                ExitCode = 0;
                return;
            }

            if (args.Contains("--uninstall"))
            {
                // Run on the thread pool: blocking the WPF dispatcher thread with
                // GetResult() deadlocks every async continuation that resumes on it.
                Task.Run(() => engine.UninstallAsync(CancellationToken.None)).GetAwaiter().GetResult();
                ExitCode = 0;
                return;
            }

            // --deploy
            Task.Run(() => engine.DeployAsync(CancellationToken.None)).GetAwaiter().GetResult();
            ExitCode = 0;
        }
        catch (RebootRequiredException ex)
        {
            Tee("[REBOOT_REQUIRED] " + ex.Message);
            ExitCode = 3; // reboot required
        }
        catch (Exception ex)
        {
            Tee("[FAILED] " + ex.Message);
            ExitCode = 1;
        }
    }

    private static StreamWriter? OpenFileLog()
    {
        try
        {
            // Next to the real exe (Environment.ProcessPath), not AppContext.BaseDirectory:
            // for single-file apps the two can differ, and CI watches the exe directory.
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
                         ?? AppContext.BaseDirectory;
            var path = Path.Combine(exeDir, "deployer-headless.log");
            return new StreamWriter(path, append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            return null; // read-only dir etc. — console output still works
        }
    }

    // Last-resort logging for exception handlers: never throws.
    internal static void EmergencyLog(object message)
    {
        try { Console.WriteLine("[EMERGENCY] " + message); } catch { }
        try { fileLogStatic?.WriteLine("[EMERGENCY] " + message); } catch { }
    }

    private static StreamWriter? fileLogStatic;

    private static DeployOptions BuildOptions(string[] args)
    {
        var opt = new DeployOptions();
        var routeIdx = Array.IndexOf(args, "--route");
        if (routeIdx >= 0 && routeIdx + 1 < args.Length)
            opt.Route = args[routeIdx + 1] == "docker" ? "docker" : "native";
        if (args.Contains("--force")) opt.Force = true;
        if (args.Contains("--keep-runtimes")) opt.KeepRuntimes = true;
        if (args.Contains("--yes")) opt.AssumeYes = true;
        if (args.Contains("--skip-preflight")) opt.SkipPreFlight = true;
        var retryIdx = Array.IndexOf(args, "--retry");
        if (retryIdx >= 0 && retryIdx + 1 < args.Length && int.TryParse(args[retryIdx + 1], out var retries) && retries >= 0)
            opt.RetryCount = retries;
        return opt;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private static void TryAttachConsole()
    {
        const int attachParentProcess = -1;
        if (!AttachConsole(attachParentProcess))
        {
            // No parent console (double-clicked) — output goes to the log file only.
        }
    }
}
