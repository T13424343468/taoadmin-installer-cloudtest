using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace TaoAdmin.Deployer.App;

public partial class App : Application
{
    // Earliest possible managed breadcrumb: runs at assembly load, before Main.
    [ModuleInitializer]
    internal static void ModuleInit()
    {
        WriteBreadcrumb("module-init (assembly loaded)");
    }

    public App()
    {
        WriteBreadcrumb("App ctor");
    }

    // Breadcrumb: proves managed code started, records the exact args received.
    // Written to the exe dir AND %TEMP% so a path quirk can never hide it.
    internal static void WriteBreadcrumb(string note)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + note
                   + " | args=[" + string.Join(" ", Environment.GetCommandLineArgs()) + "]"
                   + " | procPath=" + (Environment.ProcessPath ?? "null")
                   + " | baseDir=" + AppContext.BaseDirectory;
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
                      ?? AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(dir, "deployer-started.txt"), line);
        }
        catch { }
        try
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "deployer-started.txt"), line);
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
            // Hard exit: bypasses the dispatcher entirely so a wedged pump can never hang CI.
            Environment.Exit(HeadlessRunner.ExitCode);
        }
        // GUI mode: show the wizard window (StartupUri removed so headless never opens a window).
        WriteBreadcrumb("gui branch taken");
        new MainWindow().Show();
    }
}
