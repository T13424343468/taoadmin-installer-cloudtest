using System.Text.Json;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Alerting;
using TaoAdmin.Common.Monitoring;

namespace TaoAdmin.IdentityService.Services;

/// <summary>
/// 资源监控后台任务（②）：定时采样各服务 /metrics/resources 指标接口（走配置的 Monitor:Services 清单），
/// 结果进历史环形缓冲（近期趋势）并做阈值评估，越界/恢复经 IAlertEventPublisher 抛标准化告警事件
/// （告警接线由告警中心消费，本任务只负责抛事件）。定时器用 PeriodicTimer 独立循环，不占请求线程。
/// </summary>
public class ResourceMonitorService(
    IHttpClientFactory httpFactory,
    IConfiguration config,
    ResourceHistoryStore history,
    IAlertEventPublisher alerts,
    IServiceScopeFactory scopeFactory,
    ILogger<ResourceMonitorService> logger) : BackgroundService
{
    private readonly Dictionary<string, bool> _alerting = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>采样间隔（秒），配置 Monitor:Resource:IntervalSeconds，默认 30。</summary>
    internal int IntervalSeconds => config.GetValue<int>("Monitor:Resource:IntervalSeconds", 30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("资源监控采样启动，间隔 {Interval} 秒", IntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await SampleAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 单轮采样失败不终止后台任务，下一轮继续
                logger.LogWarning(ex, "资源采样一轮失败，等待下一轮");
            }
        }
    }

    /// <summary>采样一轮：逐服务拉取指标 → 入历史 → 阈值评估 → 抛告警事件。</summary>
    internal async Task SampleAllAsync(CancellationToken ct)
    {
        var thresholds = await GetThresholdsAsync(ct).ConfigureAwait(false);
        foreach (var (name, url) in ConfiguredServices())
        {
            var snapshot = await FetchAsync(name, url, ct).ConfigureAwait(false);
            if (snapshot == null)
                continue; // 探测失败：不入账（趋势图出现空洞即代表该时段不可达）
            history.Add(snapshot);
            await EvaluateAndAlertAsync(snapshot, thresholds).ConfigureAwait(false);
        }
    }

    /// <summary>配置的服务清单（Monitor:Services：名称 -> 健康检查地址）。</summary>
    internal List<(string Name, string Url)> ConfiguredServices()
        => config.GetSection("Monitor:Services").GetChildren()
            .Select(section => (section.Key!, section.Value!))
            .Where(pair => !string.IsNullOrEmpty(pair.Item2))
            .ToList();

    private async Task<ResourceThresholds> GetThresholdsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<ResourceThresholdProvider>();
        return await provider.GetAsync().ConfigureAwait(false);
    }

    private async Task<ResourceSnapshot?> FetchAsync(string name, string url, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("resource-monitor");
            http.Timeout = TimeSpan.FromSeconds(3);
            using var resp = await http.GetAsync(url.TrimEnd('/') + "/metrics/resources", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("资源指标拉取失败：{Service} HTTP {Status}", name, (int)resp.StatusCode);
                return null;
            }
            var snapshot = await resp.Content.ReadFromJsonAsync<ResourceSnapshot>(JsonOptions, ct).ConfigureAwait(false);
            if (snapshot == null)
            {
                logger.LogWarning("资源指标解析为空：{Service}", name);
                return null;
            }
            snapshot.ServiceName = name;
            return snapshot;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("资源指标拉取异常：{Service}，{Message}", name, ex.Message);
            return null;
        }
    }

    private async Task EvaluateAndAlertAsync(ResourceSnapshot snapshot, ResourceThresholds thresholds)
    {
        bool wasAlerting;
        lock (_stateLock)
        {
            wasAlerting = _alerting.TryGetValue(snapshot.ServiceName, out var flag) && flag;
        }
        var (evt, alerting) = ResourceThresholdEvaluator.EvaluateTransition(snapshot, thresholds, wasAlerting);
        lock (_stateLock)
        {
            _alerting[snapshot.ServiceName] = alerting;
        }
        if (evt != null)
            await alerts.PublishAsync(evt).ConfigureAwait(false);
    }
}
