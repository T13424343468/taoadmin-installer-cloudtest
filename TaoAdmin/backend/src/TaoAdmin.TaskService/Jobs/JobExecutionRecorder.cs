using Quartz;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.TaskService.Entities;

namespace TaoAdmin.TaskService.Jobs;

/// <summary>
/// 任务执行记录器：包装作业执行，每次执行（含重试）落一条 sys_job_log
/// （时间、结果、耗时、失败原因、重试序号），并回写任务的上次执行时间/结果。
/// 最终失败后按「连续失败次数」触发 JOB_FAIL 告警（阈值由告警规则配置，未达阈值不发送）。
/// 重试次数为任务级配置（sys_job.RetryCount），0 表示不重试。
/// </summary>
public class JobExecutionRecorder
{
    private readonly ISqlSugarClient _db;
    private readonly SnowflakeIdGenerator _snowflake;
    private readonly AlertNotifier _alerts;

    public JobExecutionRecorder(ISqlSugarClient db, SnowflakeIdGenerator snowflake, AlertNotifier alerts)
    {
        _db = db;
        _snowflake = snowflake;
        _alerts = alerts;
    }

    /// <summary>带记录的作业执行：成功或重试后成功落成功记录；重试耗尽落失败记录并触发告警评估。</summary>
    public async Task ExecuteWithRecordingAsync(IJob inner, IJobExecutionContext context)
    {
        var jobName = context.JobDetail.Key.Name;
        var job = await _db.Queryable<SysJob>().FirstAsync(j => j.Name == jobName && j.IsDeleted == false);
        var retries = job?.RetryCount ?? 0;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            var start = DateTime.Now;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await inner.Execute(context);
                stopwatch.Stop();
                await WriteLogAsync(job, jobName, start, stopwatch.ElapsedMilliseconds, true, null, attempt);
                await UpdateJobResultAsync(job, "执行成功");
                return;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await WriteLogAsync(job, jobName, start, stopwatch.ElapsedMilliseconds, false, ex.Message, attempt);
                if (attempt == retries)
                {
                    await UpdateJobResultAsync(job, $"执行失败：{ex.Message}");
                    var consecutive = await CountConsecutiveFailuresAsync(job?.Id ?? 0);
                    await _alerts.NotifyAsync("JOB_FAIL",
                        $"定时任务连续失败：{jobName}",
                        $"任务 {jobName} 已连续失败 {consecutive} 次，最近错误：{ex.Message}",
                        consecutive);
                }
            }
        }
    }

    private async Task WriteLogAsync(SysJob? job, string jobName, DateTime start, long durationMs,
        bool success, string? message, int retryIndex)
    {
        await _db.Insertable(new SysJobLog
        {
            Id = _snowflake.NextId(),
            JobId = job?.Id ?? 0,
            JobName = jobName,
            Group = job?.Group ?? "default",
            StartTime = start,
            EndTime = DateTime.Now,
            DurationMs = durationMs,
            Success = success,
            Message = message,
            RetryIndex = retryIndex
        }).ExecuteCommandAsync();
    }

    private async Task UpdateJobResultAsync(SysJob? job, string result)
    {
        if (job == null)
            return;
        job.LastFireTime = DateTime.Now;
        job.LastResult = result;
        await _db.Updateable(job).UpdateColumns(j => new { j.LastFireTime, j.LastResult }).ExecuteCommandAsync();
    }

    /// <summary>统计任务最近连续失败次数（从最新一条往前数，遇到成功即停）。</summary>
    public async Task<int> CountConsecutiveFailuresAsync(long jobId)
    {
        var recent = await _db.Queryable<SysJobLog>()
            .Where(l => l.JobId == jobId)
            .OrderByDescending(l => l.StartTime)
            .Take(100)
            .ToListAsync();
        var count = 0;
        foreach (var log in recent)
        {
            if (log.Success)
                break;
            count++;
        }
        return count;
    }
}

/// <summary>记录型作业装饰器：把 Quartz 触发的执行转发给记录器。</summary>
public class RecordingJob(IJob inner, JobExecutionRecorder recorder) : IJob
{
    /// <summary>被包装的原始作业（供测试与诊断核对包装关系）。</summary>
    public IJob Inner => inner;

    public Task Execute(IJobExecutionContext context)
        => recorder.ExecuteWithRecordingAsync(inner, context);
}
