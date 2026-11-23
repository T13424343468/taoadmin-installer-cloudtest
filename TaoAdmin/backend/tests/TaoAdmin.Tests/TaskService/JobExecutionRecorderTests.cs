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
/// 任务执行记录器测试（SQLite 内存库真库）：
/// 执行历史落库（成功/失败/重试）、结果回写、连续失败告警触发与阈值未到不触发。
/// </summary>
public class JobExecutionRecorderTests
{
    /// <summary>可控作业：按脚本决定每次执行成功或失败。</summary>
    private class ScriptedJob : IJob
    {
        private readonly Queue<bool> _script;
        public int Executions;

        public ScriptedJob(params bool[] outcomes) => _script = new Queue<bool>(outcomes);

        public Task Execute(IJobExecutionContext context)
        {
            Executions++;
            var ok = _script.Count > 0 ? _script.Dequeue() : true;
            if (!ok)
                throw new InvalidOperationException("作业内部错误");
            return Task.CompletedTask;
        }
    }

    /// <summary>邮件发送测试替身：记录调用。</summary>
    private class RecordingEmailSender : IEmailSender
    {
        public int Calls;
        public Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body)
        {
            Calls++;
            return Task.FromResult((true, "发送成功"));
        }
    }

    private static IJobExecutionContext Context(string name, string group)
    {
        var detail = new JobDetailImpl(name, group, typeof(ScriptedJob));
        var trigger = new SimpleTriggerImpl("t", group, DateTimeOffset.Now);
        var bundle = new TriggerFiredBundle(detail, trigger, null, false,
            DateTimeOffset.Now, DateTimeOffset.Now, null, DateTimeOffset.Now.AddMinutes(1));
        return new JobExecutionContextImpl(null!, bundle, new ScriptedJob());
    }

    private readonly SqlSugarClient _db;
    private readonly JobExecutionRecorder _recorder;
    private readonly RecordingEmailSender _email = new();

    public JobExecutionRecorderTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysJob, SysJobLog, SysAlertRule, SysNotifyLog, SysSiteConfig>();
        var notifier = new AlertNotifier(_db, new SnowflakeIdGenerator(1, 1), _email,
            new DingTalkSender(_db, new System.Net.Http.HttpClient()));
        _recorder = new JobExecutionRecorder(_db, new SnowflakeIdGenerator(2, 1), notifier);
    }

    private async Task<SysJob> SeedJob(string name = "cleanJob", int retryCount = 0)
    {
        var job = new SysJob
        {
            Id = 1000, Name = name, Group = "default", Cron = "0/30 * * * * ?",
            Handler = "heartbeatJob", RetryCount = retryCount
        };
        await _db.Insertable(job).ExecuteCommandAsync();
        return job;
    }

    private async Task SeedRule(int threshold)
    {
        await _db.Insertable(new SysAlertRule
        {
            Id = 2000, Name = "任务失败告警", EventType = "JOB_FAIL", Threshold = threshold,
            Channels = "email", Enabled = true
        }).ExecuteCommandAsync();
        await _db.Insertable(new SysSiteConfig
        {
            Id = 3000, ConfigKey = "notify_alert_receivers", ConfigValue = "admin@tao.dev"
        }).ExecuteCommandAsync();
    }

    [Fact]
    public async Task ExecuteWithRecording_Success_ShouldWriteOneLog_AndUpdateJobResult()
    {
        var job = await SeedJob();
        var inner = new ScriptedJob(true);

        await _recorder.ExecuteWithRecordingAsync(inner, Context(job.Name, job.Group));

        var log = _db.Queryable<SysJobLog>().Single();
        Assert.True(log.Success);
        Assert.Equal(job.Id, log.JobId);
        Assert.Equal(job.Name, log.JobName);
        Assert.Equal("default", log.Group);
        Assert.Equal(0, log.RetryIndex);
        Assert.Null(log.Message);
        Assert.True(log.DurationMs >= 0);
        Assert.True(log.EndTime >= log.StartTime);

        var updated = _db.Queryable<SysJob>().Single();
        Assert.Equal("执行成功", updated.LastResult);
        Assert.NotNull(updated.LastFireTime);
        Assert.Equal(0, _email.Calls);
    }

    [Fact]
    public async Task ExecuteWithRecording_FailureNoRetry_ShouldWriteFailureLog_WithReason()
    {
        var job = await SeedJob();
        var inner = new ScriptedJob(false);

        await _recorder.ExecuteWithRecordingAsync(inner, Context(job.Name, job.Group));

        var log = _db.Queryable<SysJobLog>().Single();
        Assert.False(log.Success);
        Assert.Equal("作业内部错误", log.Message);
        Assert.Equal(0, log.RetryIndex);
        Assert.Equal(1, inner.Executions);

        var updated = _db.Queryable<SysJob>().Single();
        Assert.StartsWith("执行失败：", updated.LastResult);
        Assert.Contains("作业内部错误", updated.LastResult);
    }

    [Fact]
    public async Task ExecuteWithRecording_RetryThenSuccess_ShouldLogEachAttempt()
    {
        var job = await SeedJob(retryCount: 2);
        var inner = new ScriptedJob(false, true);

        await _recorder.ExecuteWithRecordingAsync(inner, Context(job.Name, job.Group));

        Assert.Equal(2, inner.Executions);
        var logs = _db.Queryable<SysJobLog>().OrderBy(l => l.RetryIndex).ToList();
        Assert.Equal(2, logs.Count);
        Assert.False(logs[0].Success);
        Assert.Equal(0, logs[0].RetryIndex);
        Assert.True(logs[1].Success);
        Assert.Equal(1, logs[1].RetryIndex);

        var updated = _db.Queryable<SysJob>().Single();
        Assert.Equal("执行成功", updated.LastResult);
        Assert.Equal(0, _email.Calls); // 重试成功不告警
    }

    [Fact]
    public async Task ExecuteWithRecording_RetryExhausted_ShouldAlert_WhenThresholdReached()
    {
        var job = await SeedJob(retryCount: 1);
        await SeedRule(threshold: 2);
        var inner = new ScriptedJob(false, false);

        await _recorder.ExecuteWithRecordingAsync(inner, Context(job.Name, job.Group));

        Assert.Equal(2, inner.Executions);
        Assert.Equal(2, _db.Queryable<SysJobLog>().Count(l => !l.Success));
        // 连续失败 2 次达到阈值 2 → 触发邮件告警
        Assert.Equal(1, _email.Calls);
        var notifyLog = _db.Queryable<SysNotifyLog>().Single();
        Assert.True(notifyLog.Success);
        Assert.Equal("email", notifyLog.Channel);
        Assert.Contains("cleanJob", notifyLog.Subject);
    }

    [Fact]
    public async Task ExecuteWithRecording_ThresholdNotReached_ShouldNotAlert()
    {
        var job = await SeedJob(retryCount: 1);
        await SeedRule(threshold: 5);
        var inner = new ScriptedJob(false, false);

        await _recorder.ExecuteWithRecordingAsync(inner, Context(job.Name, job.Group));

        Assert.Equal(2, inner.Executions);
        Assert.Equal(0, _email.Calls);
        Assert.Equal(0, _db.Queryable<SysNotifyLog>().Count());
    }

    [Fact]
    public async Task ExecuteWithRecording_JobNotFound_ShouldStillLog_WithDefaults()
    {
        var inner = new ScriptedJob(false);

        await _recorder.ExecuteWithRecordingAsync(inner, Context("ghostJob", "default"));

        var log = _db.Queryable<SysJobLog>().Single();
        Assert.False(log.Success);
        Assert.Equal(0, log.JobId);
        Assert.Equal("ghostJob", log.JobName);
        Assert.Equal("default", log.Group);
    }

    [Fact]
    public async Task CountConsecutiveFailuresAsync_ShouldCountFromLatestUntilSuccess()
    {
        var job = await SeedJob();
        var now = DateTime.Now;
        // 顺序：成功、失败、失败（最新在前为两条失败）
        await _db.Insertable(new SysJobLog
        {
            Id = 6001, JobId = job.Id, JobName = job.Name, Group = "default", StartTime = now.AddMinutes(-3),
            EndTime = now.AddMinutes(-3), Success = true
        }).ExecuteCommandAsync();
        await _db.Insertable(new SysJobLog
        {
            Id = 6002, JobId = job.Id, JobName = job.Name, Group = "default", StartTime = now.AddMinutes(-2),
            EndTime = now.AddMinutes(-2), Success = false, Message = "错误1"
        }).ExecuteCommandAsync();
        await _db.Insertable(new SysJobLog
        {
            Id = 6003, JobId = job.Id, JobName = job.Name, Group = "default", StartTime = now.AddMinutes(-1),
            EndTime = now.AddMinutes(-1), Success = false, Message = "错误2"
        }).ExecuteCommandAsync();

        var count = await _recorder.CountConsecutiveFailuresAsync(job.Id);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountConsecutiveFailuresAsync_NoLogs_ShouldReturnZero()
    {
        var job = await SeedJob();

        var count = await _recorder.CountConsecutiveFailuresAsync(job.Id);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ExecuteWithRecording_JobNotFound_FailureExhausted_ShouldStillEvaluateAlert()
    {
        // 库里查不到任务（job 为 null）：重试耗尽路径仍要落失败记录并触发告警评估，
        // 覆盖 CountConsecutiveFailuresAsync(0) 与 NotifyAsync 的空任务分支
        await SeedRule(threshold: 1);
        var inner = new ScriptedJob(false);

        await _recorder.ExecuteWithRecordingAsync(inner, Context("ghostJob", "default"));

        Assert.Equal(1, inner.Executions);
        Assert.Equal(1, _email.Calls); // 连续失败 1 次达到阈值 1
        var notifyLog = _db.Queryable<SysNotifyLog>().Single();
        Assert.Contains("ghostJob", notifyLog.Subject);
    }
}
