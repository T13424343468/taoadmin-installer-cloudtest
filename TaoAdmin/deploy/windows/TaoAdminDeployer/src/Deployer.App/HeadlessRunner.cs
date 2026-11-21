// HeadlessRunner: CLI entry for CI and power users.
//   TaoAdminDeployer.exe --deploy --route native --yes
//   TaoAdminDeployer.exe --deploy --route docker --yes
//   TaoAdminDeployer.exe --uninstall --yes [--keep-runtimes]
//   TaoAdminDeployer.exe --status
namespace TaoAdmin.Deployer.App;

using TaoAdmin.Deployer.Core;

public static class HeadlessRunner
{
    public static int ExitCode { get; private set; }

    public static void Run(string[] args)
    {
        // WinExe has no console; attach to the parent console so CI captures output.
        TryAttachConsole();

        try
        {
            var paths = DeployPaths.FromExecutable(Environment.ProcessPath ?? AppContext.BaseDirectory);
            var engine = new DeployEngine(paths, BuildOptions(args));
            engine.Log = l => Console.WriteLine("[LOG] " + l);
            engine.StepOutput = (_, line) => Console.WriteLine("      " + line);

            if (args.Contains("--status"))
            {
                var s = engine.Status();
                foreach (var i in s.Items)
                    Console.WriteLine($"  {i.Name,-16} {(i.Running ? "运行中" : "未运行")}");
                foreach (var c in s.Containers)
                    Console.WriteLine("  容器: " + c);
                ExitCode = 0;
                return;
            }

            if (args.Contains("--uninstall"))
            {
                engine.UninstallAsync(CancellationToken.None).GetAwaiter().GetResult();
                ExitCode = 0;
                return;
            }

            // --deploy
            engine.DeployAsync(CancellationToken.None).GetAwaiter().GetResult();
            ExitCode = 0;
        }
        catch (RebootRequiredException ex)
        {
            Console.WriteLine("[REBOOT_REQUIRED] " + ex.Message);
            ExitCode = 3; // reboot required
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAILED] " + ex.Message);
            ExitCode = 1;
        }
    }

    private static DeployOptions BuildOptions(string[] args)
    {
        var opt = new DeployOptions();
        var routeIdx = Array.IndexOf(args, "--route");
        if (routeIdx >= 0 && routeIdx + 1 < args.Length)
            opt.Route = args[routeIdx + 1] == "docker" ? "docker" : "native";
        if (args.Contains("--force")) opt.Force = true;
        if (args.Contains("--keep-runtimes")) opt.KeepRuntimes = true;
        if (args.Contains("--yes")) opt.AssumeYes = true;
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
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch { }
    }
}
