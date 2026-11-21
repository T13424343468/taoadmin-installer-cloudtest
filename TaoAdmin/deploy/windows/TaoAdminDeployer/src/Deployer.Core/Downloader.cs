// Downloader: resumable-friendly HTTP download with retry and size validation.
// Handles the real-machine "file in use" pitfall by retrying after clearing stale temp files.
namespace TaoAdmin.Deployer.Core;

public static class Downloader
{
    public static async Task DownloadAsync(
        string url,
        string outFile,
        Action<string>? log = null,
        Action<long, long>? onProgress = null,
        CancellationToken ct = default,
        int maxAttempts = 3)
    {
        if (File.Exists(outFile) && new FileInfo(outFile).Length > 1024)
        {
            log?.Invoke($"已存在，跳过下载: {Path.GetFileName(outFile)}");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    log?.Invoke($"下载重试（第 {attempt} 次）……");
                    // Real-machine pitfall: installer file locked by AV scan or stale process.
                    TryClearStaleLocks(Path.GetFileName(outFile), log);
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
                }

                await DownloadOnceAsync(url, outFile, onProgress, ct);

                var size = new FileInfo(outFile).Length;
                if (size < 1024) throw new IOException($"下载校验失败（{size} 字节）: {url}");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
            {
                log?.Invoke($"下载失败: {ex.Message}");
                TryDelete(outFile);
            }
        }

        throw new IOException($"下载多次失败: {url}（请检查网络或杀毒软件是否拦截了下载）");
    }

    private static async Task DownloadOnceAsync(string url, string outFile, Action<long, long>? onProgress, CancellationToken ct)
    {
        using var http = new System.Net.Http.HttpClient();
        http.Timeout = TimeSpan.FromMinutes(30);
        using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(outFile + ".part", FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            onProgress?.Invoke(read, total);
        }
        await dst.FlushAsync(ct);
        dst.Dispose();

        if (File.Exists(outFile)) File.Delete(outFile);
        File.Move(outFile + ".part", outFile);
    }

    private static void TryDelete(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
        try { if (File.Exists(file + ".part")) File.Delete(file + ".part"); } catch { }
    }

    private static void TryClearStaleLocks(string fileName, Action<string>? log)
    {
        // Kill known stale installer processes that commonly hold file locks.
        foreach (var name in new[] { "Docker Desktop Installer", "docker", "com.docker.backend", "Docker Desktop" })
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    log?.Invoke($"结束可能占用文件的进程: {name} (PID {p.Id})");
                    p.Kill(entireProcessTree: true);
                    p.Dispose();
                }
            }
            catch { }
        }
    }
}
