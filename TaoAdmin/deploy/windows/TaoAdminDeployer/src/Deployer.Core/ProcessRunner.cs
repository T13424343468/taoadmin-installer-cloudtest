// ProcessRunner: run an external process, stream output lines to a callback.
namespace TaoAdmin.Deployer.Core;

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> Output)
{
    public bool Success => ExitCode == 0;
}

public static class ProcessRunner
{
    public static bool Available(string fileName)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            _ = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        Action<string>? onLine = null,
        IDictionary<string, string?>? env = null,
        System.Text.Encoding? outputEncoding = null,
        CancellationToken ct = default)
    {
        var output = new List<string>();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Real-machine pitfall: without an explicit encoding .NET decodes redirected
            // output with the OEM code page (GBK on Chinese Windows), while docker/dotnet/npm
            // emit UTF-8 — Chinese text in the fail-page tail turned into mojibake.
            StandardOutputEncoding = outputEncoding ?? System.Text.Encoding.UTF8,
            StandardErrorEncoding = outputEncoding ?? System.Text.Encoding.UTF8,
        };
        if (env != null)
        {
            foreach (var kv in env)
            {
                if (kv.Value is null) psi.EnvironmentVariables.Remove(kv.Key);
                else psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        process.Start();

        var outTask = PumpAsync(process.StandardOutput, line =>
        {
            lock (output) output.Add(line);
            onLine?.Invoke(line);
        }, ct);
        var errTask = PumpAsync(process.StandardError, line =>
        {
            lock (output) output.Add(line);
            onLine?.Invoke(line);
        }, ct);

        await using var _ = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        await Task.WhenAll(outTask, errTask);
        await process.WaitForExitAsync(ct);
        return new ProcessResult(process.ExitCode, output);
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> sink, CancellationToken ct)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            sink(line);
        }
    }
}
