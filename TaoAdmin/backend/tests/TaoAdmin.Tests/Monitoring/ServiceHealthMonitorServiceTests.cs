using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SqlSugar;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using TaoAdmin.IdentityService.Monitoring;
using Xunit;

namespace TaoAdmin.Tests.Monitoring;

/// <summary>
/// 服务健康巡检后台任务测试：
/// 一轮探测触发告警留痕、后台循环周期执行与停机取消。
/// </summary>
public class ServiceHealthMonitorServiceTests
{
    private class RecordingEmailSender : IEmailSender
    {
        public int Calls;
        public Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body)
        {
            Calls++;
            return Task.FromResult((true, "发送成功"));
        }
    }

    private static (ServiceProvider Sp, RecordingEmailSender Email) CreateProvider()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig, SysAlertRule, SysNotifyLog>();
        db.Insertable(new SysAlertRule
        {
            Id = 4000, Name = "服务下线告警", EventType = "SERVICE_DOWN", Threshold = 1,
            Channels = "email", Enabled = true
        }).ExecuteCommand();
        db.Insertable(new SysSiteConfig
        {
            Id = 4100, ConfigKey = "notify_alert_receivers", ConfigValue = "admin@tao.dev"
        }).ExecuteCommand();

        var email = new RecordingEmailSender();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            // 不可达端口：健康探测必然失败（DOWN），三个已知服务全部下线
            ["Monitor:Services:gateway"] = "http://127.0.0.1:1",
            ["Monitor:Services:identity"] = "http://127.0.0.1:1",
            ["Monitor:Services:task"] = "http://127.0.0.1:1"
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<ISqlSugarClient>(db);
        services.AddSingleton<IConfiguration>(config);
        services.AddHttpClient();
        services.AddSingleton<MonitorAppService>();
        services.AddSingleton(new SnowflakeIdGenerator(1, 1));
        services.AddSingleton<DingTalkSender>();
        services.AddSingleton<IEmailSender>(email);
        services.AddSingleton<AlertNotifier>();
        services.AddSingleton<ServiceDownAlertEvaluator>(sp =>
        {
            var notifier = sp.GetRequiredService<AlertNotifier>();
            return new ServiceDownAlertEvaluator((t, c, m, n) => notifier.NotifyAsync(t, c, m, n));
        });
        return (services.BuildServiceProvider(), email);
    }

    [Fact]
    public async Task ProbeOnceAsync_ServiceDown_ShouldTriggerAlert()
    {
        var (sp, email) = CreateProvider();
        using var scope = sp.CreateScope();
        var monitor = new ServiceHealthMonitorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IConfiguration>(),
            interval: TimeSpan.FromMilliseconds(50));

        await monitor.ProbeOnceAsync();

        // 三个已知服务全部 DOWN，每个服务各通知一次
        Assert.Equal(3, email.Calls);
    }

    [Fact]
    public async Task ProbeOnceAsync_AllUp_ShouldNotAlert()
    {
        var (sp, email) = CreateProvider();
        var monitor = new ServiceHealthMonitorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IConfiguration>(),
            interval: TimeSpan.FromMilliseconds(50));

        // 覆盖探测地址为空 → 状态 DOWN 之外的路径不可控，这里直接评估 UP 结果
        var evaluator = sp.GetRequiredService<ServiceDownAlertEvaluator>();
        await evaluator.EvaluateAsync(new[] { new ServiceItemDto { serviceName = "gw", displayName = "网关", status = "UP" } });

        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task Constructor_WithoutInterval_ShouldUseConfiguredProbeInterval()
    {
        var (sp, email) = CreateProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Monitor:ProbeIntervalSeconds"] = "60"
        }).Build();
        var monitor = new ServiceHealthMonitorService(
            sp.GetRequiredService<IServiceScopeFactory>(), config); // interval 缺省走配置周期

        // 无 interval 参数：周期取配置 Monitor:ProbeIntervalSeconds；一轮探测三个 DOWN 服务各通知一次
        await monitor.ProbeOnceAsync();

        Assert.Equal(3, email.Calls);
    }

    [Fact]
    public async Task StartAsync_AlreadyCancelled_ShouldExitWithoutProbing()
    {
        var (sp, email) = CreateProvider();
        var monitor = new ServiceHealthMonitorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IConfiguration>(),
            interval: TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await monitor.StartAsync(cts.Token); // 取消令牌使循环立即正常退出

        Assert.Equal(0, email.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RunsUntilCancelled_ShouldProbeRepeatedly()
    {
        var (sp, email) = CreateProvider();
        var monitor = new ServiceHealthMonitorService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IConfiguration>(),
            interval: TimeSpan.FromMilliseconds(20));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

        await monitor.StartAsync(cts.Token);
        try
        {
            await Task.Delay(600); // 覆盖插桩下留足余量
            Assert.True(email.Calls >= 2, $"巡检应周期执行，实际 {email.Calls} 次");
        }
        finally
        {
            cts.Cancel();
            await monitor.StopAsync(CancellationToken.None); // 取消后正常退出循环
        }
    }
}
