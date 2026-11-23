using Microsoft.AspNetCore.Http;
using Quartz.Impl;
using SqlSugar;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.TaskService.Controllers;
using TaoAdmin.TaskService.Entities;
using Xunit;

namespace TaoAdmin.Tests.TaskService;

/// <summary>
/// 任务控制器测试（SQLite 内存库真库）：
/// 执行历史分页接口、任务新增/编辑带重试次数、参数校验。
/// </summary>
public class JobControllerTests
{
    private static int _logId = 7000;
    private readonly SqlSugarClient _db;
    private readonly JobController _controller;

    public JobControllerTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysJob, SysJobLog>();
        _controller = new JobController(_db, new StdSchedulerFactory(), new SnowflakeIdGenerator(1, 1))
        {
            ControllerContext = { HttpContext = new DefaultHttpContext() }
        };
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

    private static SysJobLog Log(long jobId, DateTime start, bool success, string? message = null, int retryIndex = 0)
        => new()
        {
            Id = Interlocked.Increment(ref _logId), JobId = jobId, JobName = "cleanJob", Group = "default", StartTime = start,
            EndTime = start.AddSeconds(1), DurationMs = 1000, Success = success,
            Message = message, RetryIndex = retryIndex
        };

    [Fact]
    public async Task Logs_ShouldReturnPagedHistory_Descending()
    {
        var job = await SeedJob();
        var now = DateTime.Now;
        for (var i = 0; i < 3; i++)
            await _db.Insertable(Log(job.Id, now.AddMinutes(-i), success: i == 0, message: i == 0 ? null : "错误")).ExecuteCommandAsync();

        var result = await _controller.Logs(job.Id.ToString());

        Assert.True(result.Success);
        Assert.Equal(3, result.Data!.Total);
        Assert.Equal(3, result.Data.Items.Count);
        // 倒序：最新一条在前（i=0 成功）
        Assert.True(result.Data.Items[0].success);
        Assert.False(result.Data.Items[1].success);
        Assert.Equal("错误", result.Data.Items[1].message);
        Assert.Equal(job.Id.ToString(), result.Data.Items[0].jobId);
        Assert.Equal(1000, result.Data.Items[0].durationMs);
        Assert.Equal(0, result.Data.Items[0].retryIndex);
    }

    [Fact]
    public async Task Logs_EmptyHistory_ShouldReturnZeroTotal()
    {
        var job = await SeedJob();

        var result = await _controller.Logs(job.Id.ToString());

        Assert.Equal(0, result.Data!.Total);
        Assert.Empty(result.Data.Items);
    }

    [Fact]
    public async Task Logs_PageSize_ShouldLimitItems()
    {
        var job = await SeedJob();
        var now = DateTime.Now;
        for (var i = 0; i < 5; i++)
            await _db.Insertable(Log(job.Id, now.AddMinutes(-i), true)).ExecuteCommandAsync();

        var result = await _controller.Logs(job.Id.ToString(), pageIndex: 2, pageSize: 2);

        Assert.Equal(5, result.Data!.Total);
        Assert.Equal(2, result.Data.Items.Count);
    }

    [Fact]
    public async Task Page_ShouldIncludeRetryCount()
    {
        await SeedJob(retryCount: 3);

        var result = await _controller.Page();

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(3, item.retryCount);
    }

    [Fact]
    public async Task Add_WithRetryCount_ShouldPersist()
    {
        var req = new JobSaveRequest
        {
            name = "backupJobRetry", cron = "0 0 2 * * ?", handler = "heartbeatJob", retryCount = 5
        };

        var result = await _controller.Add(req);

        Assert.True(result.Success);
        var job = _db.Queryable<SysJob>().Single();
        Assert.Equal(5, job.RetryCount);
        Assert.Equal("backupJobRetry", job.Name);
    }

    [Fact]
    public async Task Add_NegativeRetryCount_ShouldClampToZero()
    {
        var req = new JobSaveRequest
        {
            name = "backupJobClamp", cron = "0 0 2 * * ?", handler = "heartbeatJob", retryCount = -3
        };

        await _controller.Add(req);

        Assert.Equal(0, _db.Queryable<SysJob>().Single().RetryCount);
    }

    [Fact]
    public async Task Update_WithRetryCount_ShouldPersist()
    {
        var job = await SeedJob();
        var req = new JobSaveRequest
        {
            name = job.Name, cron = job.Cron, handler = job.Handler, retryCount = 2
        };

        var result = await _controller.Update(job.Id.ToString(), req);

        Assert.True(result.Success);
        Assert.Equal(2, _db.Queryable<SysJob>().Single().RetryCount);
    }

    [Fact]
    public async Task Add_BlankName_ShouldThrow()
    {
        var req = new JobSaveRequest { name = " ", cron = "0 0 2 * * ?", handler = "heartbeatJob" };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _controller.Add(req));
        Assert.Equal("任务名、Cron、处理器均不能为空", ex.Message);
    }

    [Fact]
    public async Task Add_InvalidCron_ShouldThrow()
    {
        var req = new JobSaveRequest { name = "x", cron = "not-a-cron", handler = "heartbeatJob" };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _controller.Add(req));
        Assert.Equal("Cron 表达式不合法", ex.Message);
    }

    [Fact]
    public async Task Add_UnknownHandler_ShouldThrow()
    {
        var req = new JobSaveRequest { name = "x", cron = "0 0 2 * * ?", handler = "noSuchJob" };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _controller.Add(req));
        Assert.Contains("未注册的处理器", ex.Message);
    }

    [Fact]
    public async Task Update_BlankCron_ShouldThrow()
    {
        var job = await SeedJob();
        var req = new JobSaveRequest { name = job.Name, cron = " ", handler = job.Handler };

        var ex = await Assert.ThrowsAsync<DomainException>(() => _controller.Update(job.Id.ToString(), req));
        Assert.Equal("任务名、Cron、处理器均不能为空", ex.Message);
    }
}
