using TaoAdmin.Common.Cache;
using Quartz;

namespace TaoAdmin.TaskService.Jobs;

/// <summary>心跳示例作业：向 Redis 写入服务心跳（执行历史与结果回写由 JobExecutionRecorder 统一处理）</summary>
public class HeartbeatJob : IJob
{
    private readonly RedisHelper _redis;

    public HeartbeatJob(RedisHelper redis)
    {
        _redis = redis;
    }

    public Task Execute(IJobExecutionContext context)
    {
        var key = $"heartbeat:{context.JobDetail.Key.Name}";
        _redis.SetString(key, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), TimeSpan.FromMinutes(10));
        return Task.CompletedTask;
    }
}
