using TaoAdmin.Common.Http;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 操作日志查询服务：分页检索操作/登录审计记录，供管理后台追溯。
/// 亿级优化：①筛选列二级索引（见实体 SugarIndex）；②总数按筛选条件缓存 5 分钟，不再每页实数；
/// ③深翻页走游标（Keyset）定位，翻到任意深度都是恒定速度，不再随页码线性变慢。
/// </summary>
public class OperationLogAppService(ISqlSugarClient db, IMemoryCache cache)
{
    /// <summary>分页查询（类别/账号/结果/时间范围筛选；cursor 为上一页返回的 NextCursor 时走游标翻页）</summary>
    public async Task<PageResult<SysOperationLog>> PageAsync(
        int pageIndex, int pageSize, string? category, string? userName, int? success, DateTime? begin, DateTime? end,
        string? cursor = null)
    {
        pageIndex = Math.Max(pageIndex, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), 100);

        var query = db.Queryable<SysOperationLog>()
            .Where(l => l.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(category), l => l.Category == category)
            .WhereIF(!string.IsNullOrWhiteSpace(userName), l => l.UserName.Contains(userName!))
            .WhereIF(success.HasValue, l => l.Success == success!.Value)
            .WhereIF(begin.HasValue, l => l.CreatedAt >= begin!.Value)
            .WhereIF(end.HasValue, l => l.CreatedAt <= end!.Value);

        // 总数按筛选条件缓存 5 分钟：亿级表上 COUNT 全表代价高，翻页期间复用同一口径
        var cacheKey = $"oplog:total:{category}|{userName}|{success}|{begin?.Ticks}|{end?.Ticks}";
        var total = await cache.GetOrCreateAsync(cacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return (long)await query.CountAsync();
        });

        // 游标翻页：按 (CreatedAt, Id) 复合键定位，索引直达起点后顺序取一页
        var cur = ParseCursor(cursor);
        if (cur.HasValue)
        {
            query = query.Where(l => l.CreatedAt < cur!.Value.time
                                     || (l.CreatedAt == cur.Value.time && l.Id < cur.Value.id));
        }

        var list = await query
            .OrderBy(l => l.CreatedAt, OrderByType.Desc)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .Take(pageSize)
            .ToListAsync();

        // 产出下一页游标：本页最后一条的 (CreatedAt, Id)；不足一页说明到底了
        string? nextCursor = list.Count == pageSize && list.Count > 0
            ? EncodeCursor(list[^1].CreatedAt, list[^1].Id)
            : null;
        return new PageResult<SysOperationLog>
        {
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
            Items = list,
            NextCursor = nextCursor
        };
    }

    /// <summary>游标格式：Base64("CreatedAt.Ticks|Id")；解析失败按无游标处理（回退 OFFSET 翻页）</summary>
    internal static string EncodeCursor(DateTime createdAt, long id)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{createdAt.Ticks}|{id}"));

    internal static (DateTime time, long id)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !long.TryParse(parts[1], out var id))
                return null;
            return (new DateTime(ticks), id);
        }
        catch (FormatException)
        {
            return null;
        }
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
