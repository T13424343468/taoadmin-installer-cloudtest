using Microsoft.Extensions.Hosting;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;

namespace TaoAdmin.IdentityService.Monitoring;

/// <summary>
/// 服务健康巡检后台任务：周期性探测受监控服务，服务不可用时经 ServiceDownAlertEvaluator
/// 按连续失败次数触发告警（阈值由告警规则配置）。探测周期 Monitor:ProbeIntervalSeconds（默认 60 秒）。
/// </summary>
public class ServiceHealthMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval;

    public ServiceHealthMonitorService(IServiceScopeFactory scopeFactory, IConfiguration config,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory;
        _interval = interval ?? TimeSpan.FromSeconds(config.GetValue<int>("Monitor:ProbeIntervalSeconds", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 停机时 WaitForNextTickAsync 抛 OperationCanceledException，无正常返回 false 的路径
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (true)
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await ProbeOnceAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机：等待下一个周期时被取消
        }
    }

    /// <summary>执行一轮探测与告警评估（独立作用域取服务，异常不吞）。</summary>
    public async Task ProbeOnceAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var monitor = scope.ServiceProvider.GetRequiredService<MonitorAppService>();
        var evaluator = scope.ServiceProvider.GetRequiredService<ServiceDownAlertEvaluator>();
        List<ServiceItemDto> services = await monitor.GetServicesAsync();
        await evaluator.EvaluateAsync(services);
    }
}
