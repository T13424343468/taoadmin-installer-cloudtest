// Retry: automatic retry with exponential backoff for build/publish steps.
// Boss order 2026-11-25 (deployer hardening): transient failures (network blips,
// file locks, OOM under parallel MSBuild) must be retried before the step is
// judged red. Default 2 retries with exponential backoff; can be disabled via
// DeployOptions.RetryCount = 0.
namespace TaoAdmin.Deployer.Core;

public sealed class RetryOptions
{
    /// <summary>Number of retries AFTER the first attempt (0 = no retry).</summary>
    public int Retries { get; init; } = 2;

    /// <summary>Base delay for the exponential backoff (attempt 1 → base, attempt 2 → 2×base …).</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(3);
}

public static class Retry
{
    /// <summary>
    /// Run <paramref name="action"/>; on failure retry up to <paramref name="options"/>.Retries
    /// times with exponential backoff. Re-throws the LAST exception when all attempts fail.
    /// Cancellation is never retried.
    /// </summary>
    public static async Task RunAsync(RetryOptions options, Action<string> log, Func<Task> action, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < options.Retries)
            {
                var delay = TimeSpan.FromMilliseconds(options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                log($"第 {attempt + 1} 次尝试失败（{ex.Message}），{delay.TotalSeconds:0} 秒后自动重试（共 {options.Retries + 1} 次）……");
                await Task.Delay(delay, ct);
            }
        }
    }
}
