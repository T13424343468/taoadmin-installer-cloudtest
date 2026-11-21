using System.Windows;

namespace TaoAdmin.Deployer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Headless mode: --deploy / --uninstall / --status run the engine without UI (for CI and power users).
        var args = e.Args;
        if (args.Any(a => a is "--deploy" or "--uninstall" or "--status"))
        {
            HeadlessRunner.Run(args);
            Shutdown(HeadlessRunner.ExitCode);
        }
    }
}
