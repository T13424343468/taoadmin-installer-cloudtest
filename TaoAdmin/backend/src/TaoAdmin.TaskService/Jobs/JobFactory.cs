using Quartz;
using Quartz.Spi;

namespace TaoAdmin.TaskService.Jobs;

/// <summary>
/// 作业工厂：按数据库中的 JobType 反射实例化 Quartz 作业
/// </summary>
public class JobFactory : IJobFactory
{
    private readonly IServiceProvider _sp;

    public JobFactory(IServiceProvider sp) => _sp = sp;

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobType = bundle.JobDetail.JobType;
        var svc = _sp.GetService(jobType) as IJob;
        return svc ?? (IJob)ActivatorUtilities.CreateInstance(_sp, jobType);
    }

    public void ReturnJob(IJob job) { }
}
