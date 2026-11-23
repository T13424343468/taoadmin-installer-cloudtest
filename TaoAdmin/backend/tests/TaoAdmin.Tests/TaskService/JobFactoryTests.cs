using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using Quartz.Impl.Triggers;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using TaoAdmin.TaskService.Entities;
using TaoAdmin.TaskService.Jobs;
using Xunit;

namespace TaoAdmin.Tests.TaskService;

/// <summary>
/// 作业工厂测试：注册记录器时包装 RecordingJob，未注册时直接执行；
/// 已注册类型走 DI 解析，未注册类型走反射激活。
/// </summary>
public class JobFactoryTests
{
    private class PlainJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    /// <summary>已注册进 DI 的作业（无外部依赖，便于构造）。</summary>
    private class RegisteredJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private static (ServiceProvider Sp, SqlSugarClient Db) CreateProvider(bool withRecorder)
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysJob, SysJobLog, SysAlertRule, SysNotifyLog, SysSiteConfig>();
        var services = new ServiceCollection();
        services.AddSingleton<ISqlSugarClient>(db);
        services.AddSingleton(new SnowflakeIdGenerator(1, 1));
        services.AddScoped<RegisteredJob>();
        if (withRecorder)
        {
            services.AddSingleton<DingTalkSender>();
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
            services.AddSingleton<AlertNotifier>();
            services.AddSingleton<JobExecutionRecorder>();
        }
        return (services.BuildServiceProvider(), db);
    }

    private static TriggerFiredBundle Bundle(Type jobType)
    {
        var detail = new JobDetailImpl("testJob", "default", jobType);
        var trigger = new SimpleTriggerImpl("testTrigger", "default", DateTimeOffset.Now);
        return new TriggerFiredBundle(detail, trigger, null, false,
            DateTimeOffset.Now, DateTimeOffset.Now, null, DateTimeOffset.Now.AddMinutes(1));
    }

    [Fact]
    public void NewJob_WithRecorder_ShouldReturnRecordingJob()
    {
        var (sp, _) = CreateProvider(withRecorder: true);
        var factory = new JobFactory(sp);

        var job = factory.NewJob(Bundle(typeof(RegisteredJob)), null!);

        var recording = Assert.IsType<RecordingJob>(job);
        Assert.IsType<RegisteredJob>(recording.Inner);
    }

    [Fact]
    public void NewJob_WithoutRecorder_ShouldReturnInnerJobDirectly()
    {
        var (sp, _) = CreateProvider(withRecorder: false);
        var factory = new JobFactory(sp);

        var job = factory.NewJob(Bundle(typeof(RegisteredJob)), null!);

        Assert.IsType<RegisteredJob>(job);
        Assert.IsNotType<RecordingJob>(job);
    }

    [Fact]
    public void NewJob_UnregisteredType_ShouldActivateByReflection()
    {
        var (sp, _) = CreateProvider(withRecorder: false);
        var factory = new JobFactory(sp);

        var job = factory.NewJob(Bundle(typeof(PlainJob)), null!);

        Assert.IsType<PlainJob>(job);
    }

    [Fact]
    public void NewJob_UnregisteredTypeWithRecorder_ShouldWrapReflectionActivatedJob()
    {
        // 反射激活 + 记录器包装同时命中：覆盖 svc 为空且 recorder 非空的组合分支
        var (sp, _) = CreateProvider(withRecorder: true);
        var factory = new JobFactory(sp);

        var job = factory.NewJob(Bundle(typeof(PlainJob)), null!);

        var recording = Assert.IsType<RecordingJob>(job);
        Assert.IsType<PlainJob>(recording.Inner);
    }

    [Fact]
    public void ReturnJob_ShouldCompleteWithoutAction()
    {
        // ReturnJob 为显式空实现（Quartz 契约方法），调用不抛异常即覆盖
        var (sp, _) = CreateProvider(withRecorder: false);
        var factory = new JobFactory(sp);

        factory.ReturnJob(new PlainJob());
    }
}
