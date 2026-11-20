using TaoAdmin.Common.Http;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 操作日志查询服务：分页检索操作/登录审计记录，供管理后台追溯。
/// </summary>
public class OperationLogAppService(ISqlSugarClient db)
{
    /// <summary>分页查询（类别/账号/结果/时间范围筛选）</summary>
    public async Task<PageResult<SysOperationLog>> PageAsync(
        int pageIndex, int pageSize, string? category, string? userName, int? success, DateTime? begin, DateTime? end)
    {
        RefAsync<int> total = 0;
        var list = await db.Queryable<SysOperationLog>()
            .Where(l => l.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(category), l => l.Category == category)
            .WhereIF(!string.IsNullOrWhiteSpace(userName), l => l.UserName.Contains(userName!))
            .WhereIF(success.HasValue, l => l.Success == success!.Value)
            .WhereIF(begin.HasValue, l => l.CreatedAt >= begin!.Value)
            .WhereIF(end.HasValue, l => l.CreatedAt <= end!.Value)
            .OrderBy(l => l.CreatedAt, OrderByType.Desc)
            .ToPageListAsync(Math.Max(pageIndex, 1), Math.Min(Math.Max(pageSize, 1), 100), total);
        return new PageResult<SysOperationLog>
        {
            Total = total,
            PageIndex = Math.Max(pageIndex, 1),
            PageSize = Math.Min(Math.Max(pageSize, 1), 100),
            Items = list
        };
    }
}

/// <summary>
/// 日志清理服务：按保留天数批量删除过期操作日志（每批 5000 循环），0=永久保留一条不删。
/// 定时入口见 LogCleanupBackgroundService，手工入口见 POST /audit/cleanup。
/// </summary>
public class AuditCleanupService(ISqlSugarClient db, ILogger<AuditCleanupService> logger)
{
    private const int BatchSize = 5000;

    /// <summary>执行一轮清理：返回删除总条数。retentionDays=0 时直接返回 0（永久保留）。</summary>
    public async Task<long> CleanupAsync(int retentionDays)
    {
        if (retentionDays <= 0) return 0;
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        long deleted = 0;
        while (true)
        {
            var n = await db.Queryable<SysOperationLog>()
                .Where(l => l.IsDeleted == false && l.CreatedAt < cutoff)
                .Take(BatchSize)
                .ToListAsync();
            if (n.Count == 0) break;
            var ids = n.Select(x => x.Id).ToList();
            await db.Deleteable<SysOperationLog>().In(ids).ExecuteCommandAsync();
            deleted += ids.Count;
            if (ids.Count < BatchSize) break;
        }
        if (deleted > 0)
            logger.LogInformation("日志清理完成：保留 {Days} 天，删除 {Count} 条（截止 {Cutoff}）", retentionDays, deleted, cutoff);
        return deleted;
    }
}

/// <summary>
/// 日志清理后台任务：每 6 小时核一次（启动 10 分钟后首跑），按登录安全设置中的保留天数清理。
/// 设置为 0（永久保留）时本轮直接跳过。
/// </summary>
public class LogCleanupBackgroundService(
    IServiceProvider services,
    ILogger<LogCleanupBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(FirstDelay, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var security = scope.ServiceProvider.GetRequiredService<LoginSecurityAppService>();
                var cleanup = scope.ServiceProvider.GetRequiredService<AuditCleanupService>();
                var settings = await security.GetAsync();
                if (settings.LogRetentionDays > 0)
                    await cleanup.CleanupAsync(settings.LogRetentionDays);
            }
            catch (Exception ex)
            {
                // 清理失败不拖垮宿主：记错误，下个周期重试
                logger.LogError(ex, "日志清理后台任务执行失败，等待下个周期重试");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }
}
