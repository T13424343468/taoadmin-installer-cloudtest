using System.IO;
using System.Windows;

namespace TaoAdmin.Deployer.App;

public partial class App : Application
{
    // Breadcrumb: proves managed code started, records the exact args received.
    // Written before anything else so a startup hang is distinguishable from a pre-Main hang.
    internal static void WriteBreadcrumb(string note)
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
                      ?? AppContext.BaseDirectory;
            File.WriteAllText(
                Path.Combine(dir, "deployer-started.txt"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + note
                    + " | args=[" + string.Join(" ", Environment.GetCommandLineArgs()) + "]"
                    + " | baseDir=" + AppContext.BaseDirectory);
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        WriteBreadcrumb("OnStartup entered");
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            HeadlessRunner.EmergencyLog("DISPATCHER_UNHANDLED: " + ex.Exception);
            ex.Handled = true;
            Environment.ExitCode = 1;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            HeadlessRunner.EmergencyLog("APPDOMAIN_UNHANDLED: " + ex.ExceptionObject);
        };

        // Headless mode: --deploy / --uninstall / --status run the engine without UI (for CI and power users).
        var args = e.Args;
        if (args.Any(a => a is "--deploy" or "--uninstall" or "--status"))
        {
            WriteBreadcrumb("headless branch taken");
            HeadlessRunner.Run(args);
            WriteBreadcrumb("headless finished, exit=" + HeadlessRunner.ExitCode);
            Shutdown(HeadlessRunner.ExitCode);
            return;
        }
        // GUI mode: show the wizard window (StartupUri removed so headless never opens a window).
        new MainWindow().Show();
    }
}
