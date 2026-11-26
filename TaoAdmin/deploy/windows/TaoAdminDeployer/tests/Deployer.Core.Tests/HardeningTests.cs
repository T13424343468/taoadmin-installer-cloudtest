using Xunit;
// Deployer hardening tests (boss order 2026-11-25): retry + preflight + failure log.
using TaoAdmin.Deployer.Core;

namespace TaoAdmin.Deployer.Core.Tests;

public class RetryTests
{
    [Fact]
    public async Task SucceedsOnFirstAttempt_NoRetry()
    {
        var calls = 0;
        await Retry.RunAsync(new RetryOptions { Retries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) },
            _ => { }, () => { calls++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetriesThenSucceeds()
    {
        var calls = 0;
        await Retry.RunAsync(new RetryOptions { Retries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) },
            _ => { },
            () =>
            {
                calls++;
                if (calls < 3) throw new InvalidOperationException("transient");
                return Task.CompletedTask;
            }, CancellationToken.None);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ExhaustsRetries_ThrowsLastException()
    {
        var calls = 0;
        var logs = new List<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry.RunAsync(new RetryOptions { Retries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) },
                logs.Add,
                () =>
                {
                    calls++;
                    throw new InvalidOperationException($"boom-{calls}");
                }, CancellationToken.None));
        Assert.Equal(3, calls); // 1 initial + 2 retries
        Assert.Contains(logs, l => l.Contains("boom-2")); // logged the second failure before the last attempt
    }

    [Fact]
    public async Task ZeroRetries_FailsImmediately()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Retry.RunAsync(new RetryOptions { Retries = 0, BaseDelay = TimeSpan.FromMilliseconds(1) },
                _ => { },
                () => { calls++; throw new InvalidOperationException("no-retry"); }, CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_IsNotRetried()
    {
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Retry.RunAsync(new RetryOptions { Retries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) },
                _ => { },
                () => { calls++; throw new OperationCanceledException(); }, CancellationToken.None));
        Assert.Equal(1, calls);
    }
}

public class PreFlightTests
{
    private static DeployPaths Paths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new DeployPaths(dir);
    }

    [Fact]
    public async Task CheckAsync_ReturnsChecklist_AllCategories()
    {
        var paths = Paths();
        try
        {
            var items = await PreFlight.CheckAsync(paths, new PreFlightOptions { ProbeUrls = Array.Empty<string>() }, CancellationToken.None);
            Assert.Contains(items, i => i.Name.Contains("系统盘"));
            Assert.Contains(items, i => i.Name.Contains("源码所在盘"));
            Assert.Contains(items, i => i.Name.Contains(".NET 10 SDK"));
            Assert.Contains(items, i => i.Name.Contains("Docker 引擎"));
            Assert.Contains(items, i => i.Name == "端口 5000");
            Assert.Contains(items, i => i.Name == "端口 8080");
        }
        finally { try { Directory.Delete(paths.WinDir, true); } catch { } }
    }

    [Fact]
    public async Task CheckAsync_OccupiedPort_FailsWithDetail()
    {
        var paths = Paths();
        try
        {
            // Occupy a port with a real listener.
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                var items = await PreFlight.CheckAsync(paths,
                    new PreFlightOptions { Ports = new[] { port }, ProbeUrls = Array.Empty<string>() },
                    CancellationToken.None);
                var item = items.Single(i => i.Name == $"端口 {port}");
                Assert.False(item.Passed);
                Assert.Contains("已被占用", item.Detail);
            }
            finally { listener.Stop(); }
        }
        finally { try { Directory.Delete(paths.WinDir, true); } catch { } }
    }

    [Fact]
    public async Task CheckAsync_FreePort_Passes()
    {
        var paths = Paths();
        try
        {
            var items = await PreFlight.CheckAsync(paths,
                new PreFlightOptions { Ports = new[] { 59999 }, ProbeUrls = Array.Empty<string>() },
                CancellationToken.None);
            var item = items.Single(i => i.Name == "端口 59999");
            Assert.True(item.Passed);
        }
        finally { try { Directory.Delete(paths.WinDir, true); } catch { } }
    }

    [Fact]
    public async Task CheckAsync_UnreachableUrl_Fails()
    {
        var paths = Paths();
        try
        {
            var items = await PreFlight.CheckAsync(paths,
                new PreFlightOptions { Ports = Array.Empty<int>(), ProbeUrls = new[] { "http://127.0.0.1:1/nothing" } },
                CancellationToken.None);
            var item = items.Single(i => i.Name.Contains("127.0.0.1"));
            Assert.False(item.Passed);
            Assert.Contains("不可达", item.Detail);
        }
        finally { try { Directory.Delete(paths.WinDir, true); } catch { } }
    }
}

public class ProcessRunnerTraceTests
{
    [Fact]
    public async Task RunAsync_RecordsLastCommandAndExitCode()
    {
        var r = await ProcessRunner.RunAsync("dotnet", "--help");
        Assert.True(r.Success);
        Assert.Contains("dotnet", ProcessRunner.LastCommand);
        Assert.Contains("--help", ProcessRunner.LastCommand);
        Assert.Equal(0, ProcessRunner.LastExitCode);
    }

    [Fact]
    public async Task RunAsync_FailingCommand_RecordsNonZeroExitCode()
    {
        var r = await ProcessRunner.RunAsync("dotnet", "this-is-not-a-command-xyz");
        Assert.False(r.Success);
        Assert.NotEqual(0, ProcessRunner.LastExitCode);
        Assert.Contains("this-is-not-a-command-xyz", ProcessRunner.LastCommand);
    }
}
