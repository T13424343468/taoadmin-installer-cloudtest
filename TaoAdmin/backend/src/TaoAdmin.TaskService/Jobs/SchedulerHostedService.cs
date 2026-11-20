using TaoAdmin.TaskService.Entities;
using Quartz;
using Quartz.Spi;
using SqlSugar;

namespace TaoAdmin.TaskService.Jobs;

/// <summary>
/// 宿主启动时：启动 Quartz 调度器，把数据库中 RUNNING 状态的任务恢复注册。
/// </summary>
public class SchedulerHostedService : IHostedService
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ISqlSugarClient _db;
    private readonly IJobFactory _jobFactory;
    private IScheduler? _scheduler;

    public SchedulerHostedService(ISchedulerFactory schedulerFactory, ISqlSugarClient db, IJobFactory jobFactory)
    {
        _schedulerFactory = schedulerFactory;
        _db = db;
        _jobFactory = jobFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        _scheduler.JobFactory = _jobFactory;
        await _scheduler.Start(cancellationToken);

        var jobs = await _db.Queryable<SysJob>().Where(j => j.Status == "RUNNING" && j.IsDeleted == false).ToListAsync();
        foreach (var job in jobs)
        {
            var type = Controllers.HandlerRegistry.Resolve(job.Handler);
            if (type == null) continue;

            var detail = JobBuilder.Create(type).WithIdentity(job.Name, job.Group).Build();
            var trigger = TriggerBuilder.Create()
                .WithIdentity($"{job.Name}.trigger", job.Group)
                .ForJob(detail)
                .WithCronSchedule(job.Cron)
                .Build();
            await _scheduler.ScheduleJob(detail, trigger, cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler != null) await _scheduler.Shutdown(cancellationToken);
    }
}
