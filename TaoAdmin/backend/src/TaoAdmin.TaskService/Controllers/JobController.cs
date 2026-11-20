using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using TaoAdmin.TaskService.Entities;
using Microsoft.AspNetCore.Mvc;
using Quartz;
using SqlSugar;

namespace TaoAdmin.TaskService.Controllers;

/// <summary>
/// 定时任务管理：任务 CRUD、暂停/恢复/立即执行、Cron 校验。
/// 处理器标识 -> IJob 映射见 HandlerRegistry。
/// </summary>
[Route("jobs")]
public class JobController : BaseApiController
{
    private readonly ISqlSugarClient _db;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public JobController(ISqlSugarClient db, ISchedulerFactory schedulerFactory,
        Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _schedulerFactory = schedulerFactory; _snowflake = snowflake;
    }

    /// <summary>分页查询任务</summary>
    [HttpGet]
    [Permission("sys:job:list")]
    public async Task<ApiResult<PageResult<JobItemDto>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
    {
        var query = _db.Queryable<SysJob>()
            .WhereIF(!string.IsNullOrEmpty(keyword), j => j.Name.Contains(keyword!) || j.Handler.Contains(keyword!));
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(j => j.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();

        var items = rows.Select(j => new JobItemDto
        {
            id = j.Id.ToString(),
            name = j.Name,
            group = j.Group,
            cron = j.Cron,
            handler = j.Handler,
            @params = j.Params,
            status = j.Status,
            description = j.Description,
            lastFireTime = j.LastFireTime?.ToString("yyyy-MM-dd HH:mm:ss"),
            nextFireTime = j.NextFireTime?.ToString("yyyy-MM-dd HH:mm:ss"),
            lastResult = j.LastResult,
            createdAt = j.CreatedAt
        }).ToList();
        return ApiOk(new PageResult<JobItemDto>(total, pageIndex, pageSize, items));
    }

    /// <summary>新增任务</summary>
    [HttpPost]
    [Permission("sys:job:add")]
    public async Task<ApiResult<object>> Add([FromBody] JobSaveRequest req)
    {
        var job = BuildJob(req, isNew: true);
        await _db.Insertable(job).ExecuteCommandAsync();
        await ScheduleAsync(job);
        return ApiOk("新增成功");
    }

    /// <summary>编辑任务</summary>
    [HttpPut("{id}")]
    [Permission("sys:job:edit")]
    public async Task<ApiResult<object>> Update(string id, [FromBody] JobSaveRequest req)
    {
        var job = await _db.Queryable<SysJob>().InSingleAsync(long.Parse(id));
        UpdateJob(job, req);
        await _db.Updateable(job)
            .UpdateColumns(j => new { j.Name, j.Group, j.Cron, j.Handler, j.Params, j.Description, j.UpdatedAt })
            .ExecuteCommandAsync();
        await UnscheduleAsync(job.Name, job.Group);
        if (job.Status == "RUNNING") await ScheduleAsync(job);
        return ApiOk("修改成功");
    }

    /// <summary>删除任务</summary>
    [HttpDelete("{id}")]
    [Permission("sys:job:delete")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        var job = await _db.Queryable<SysJob>().InSingleAsync(long.Parse(id));
        await UnscheduleAsync(job.Name, job.Group);
        await _db.Deleteable<SysJob>().In(job.Id).ExecuteCommandAsync();
        return ApiOk("删除成功");
    }

    /// <summary>暂停任务</summary>
    [HttpPost("{id}/pause")]
    [Permission("sys:job:pause")]
    public async Task<ApiResult<object>> Pause(string id)
    {
        var job = await _db.Queryable<SysJob>().InSingleAsync(long.Parse(id));
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.PauseJob(new JobKey(job.Name, job.Group));
        job.Status = "PAUSED";
        await _db.Updateable(job).UpdateColumns(j => new { j.Status, j.UpdatedAt }).ExecuteCommandAsync();
        return ApiOk("已暂停");
    }

    /// <summary>恢复任务</summary>
    [HttpPost("{id}/resume")]
    [Permission("sys:job:resume")]
    public async Task<ApiResult<object>> Resume(string id)
    {
        var job = await _db.Queryable<SysJob>().InSingleAsync(long.Parse(id));
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.ResumeJob(new JobKey(job.Name, job.Group));
        job.Status = "RUNNING";
        await _db.Updateable(job).UpdateColumns(j => new { j.Status, j.UpdatedAt }).ExecuteCommandAsync();
        return ApiOk("已恢复");
    }

    /// <summary>立即执行一次</summary>
    [HttpPost("{id}/run")]
    [Permission("sys:job:run")]
    public async Task<ApiResult<object>> Run(string id)
    {
        var job = await _db.Queryable<SysJob>().InSingleAsync(long.Parse(id));
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey(job.Name, job.Group));
        return ApiOk("已触发执行");
    }

    /// <summary>校验 Cron 表达式并返回下次触发时间</summary>
    [HttpGet("validate-cron")]
    [Permission("sys:job:list")]
    public ApiResult<object> ValidateCron([FromQuery] string cron)
    {
        try
        {
            if (!CronExpression.IsValidExpression(cron))
                return ApiOk<object>(new { valid = false, message = "Cron 表达式不合法" });
            var next = new CronExpression(cron).GetNextValidTimeAfter(DateTimeOffset.UtcNow);
            return ApiOk<object>(new { valid = true, nextFireTime = next?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") });
        }
        catch (Exception ex)
        {
            return ApiOk<object>(new { valid = false, message = ex.Message });
        }
    }

    // ---------- 内部 ----------

    private SysJob BuildJob(JobSaveRequest req, bool isNew)
    {
        if (string.IsNullOrWhiteSpace(req.name) || string.IsNullOrWhiteSpace(req.cron) || string.IsNullOrWhiteSpace(req.handler))
            throw new TaoAdmin.Domain.DomainException("任务名、Cron、处理器均不能为空");
        if (!CronExpression.IsValidExpression(req.cron))
            throw new TaoAdmin.Domain.DomainException("Cron 表达式不合法");
        var job = new SysJob
        {
            Name = req.name,
            Group = string.IsNullOrEmpty(req.group) ? "default" : req.group,
            Cron = req.cron,
            Handler = req.handler,
            Params = req.@params,
            Description = req.description,
            Status = "RUNNING"
        };
        if (isNew) job.Id = _snowflake.NextId();
        return job;
    }

    private void UpdateJob(SysJob job, JobSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.name) || string.IsNullOrWhiteSpace(req.cron) || string.IsNullOrWhiteSpace(req.handler))
            throw new TaoAdmin.Domain.DomainException("任务名、Cron、处理器均不能为空");
        if (!CronExpression.IsValidExpression(req.cron))
            throw new TaoAdmin.Domain.DomainException("Cron 表达式不合法");
        job.Name = req.name;
        job.Group = string.IsNullOrEmpty(req.group) ? "default" : req.group;
        job.Cron = req.cron;
        job.Handler = req.handler;
        job.Params = req.@params;
        job.Description = req.description;
    }

    private async Task ScheduleAsync(SysJob job)
    {
        var type = HandlerRegistry.Resolve(job.Handler)
            ?? throw new TaoAdmin.Domain.DomainException($"未注册的处理器：{job.Handler}");
        var scheduler = await _schedulerFactory.GetScheduler();
        var detail = JobBuilder.Create(type).WithIdentity(job.Name, job.Group).Build();
        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{job.Name}.trigger", job.Group)
            .ForJob(detail)
            .WithCronSchedule(job.Cron)
            .Build();
        await scheduler.ScheduleJob(detail, trigger);
    }

    private async Task UnscheduleAsync(string name, string group)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.DeleteJob(new JobKey(name, group));
    }
}

/// <summary>处理器标识 -> IJob 类型映射</summary>
public static class HandlerRegistry
{
    private static readonly Dictionary<string, Type> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["heartbeatJob"] = typeof(Jobs.HeartbeatJob)
    };

    public static Type? Resolve(string handler)
        => Map.TryGetValue(handler, out var type) ? type : Type.GetType(handler, throwOnError: false);
}
