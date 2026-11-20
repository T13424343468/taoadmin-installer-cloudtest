using TaoAdmin.Common.Cache;
using TaoAdmin.TaskService.Entities;
using Quartz;
using SqlSugar;

namespace TaoAdmin.TaskService.Jobs;

/// <summary>心跳示例作业：向 Redis 写入心跳并回写执行结果</summary>
public class HeartbeatJob : IJob
{
    private readonly RedisHelper _redis;
    private readonly ISqlSugarClient _db;

    public HeartbeatJob(RedisHelper redis, ISqlSugarClient db)
    {
        _redis = redis;
        _db = db;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var key = $"heartbeat:{context.JobDetail.Key.Name}";
        _redis.SetString(key, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), TimeSpan.FromMinutes(10));

        var db = _db.AsTenant().GetConnectionScope("default");
        var jobRow = await db.Queryable<SysJob>().FirstAsync(j => j.Name == context.JobDetail.Key.Name);
        if (jobRow != null)
        {
            jobRow.LastFireTime = DateTime.Now;
            jobRow.LastResult = "执行成功";
            await db.Updateable(jobRow).UpdateColumns(j => new { j.LastFireTime, j.LastResult }).ExecuteCommandAsync();
        }
    }
}
