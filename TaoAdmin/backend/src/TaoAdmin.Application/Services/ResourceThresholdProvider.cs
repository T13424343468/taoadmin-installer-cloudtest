using Microsoft.Extensions.Caching.Memory;
using SqlSugar;
using TaoAdmin.Common.Monitoring;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 资源监控阈值提供口：阈值存 sys_site_config（monitorCpuThreshold/monitorMemoryThreshold/
/// monitorDiskThreshold），监控页可改，保存即清缓存立即生效；未配置返回内置默认值。
/// </summary>
public class ResourceThresholdProvider(ISqlSugarClient db, SnowflakeIdGenerator snowflake, IMemoryCache cache)
{
    public const double DefaultCpuPercent = 80;
    public const double DefaultMemoryPercent = 85;
    public const double DefaultDiskPercent = 90;

    public const string KeyCpu = "monitorCpuThreshold";
    public const string KeyMemory = "monitorMemoryThreshold";
    public const string KeyDisk = "monitorDiskThreshold";

    public const string CacheKey = "sys:resource-thresholds:v1";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>读取当前阈值（命中缓存不落库；未配置的键返回默认值，越界值回退默认）。</summary>
    public async Task<ResourceThresholds> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out ResourceThresholds? cached) && cached != null)
            return cached;

        var rows = await db.Queryable<SysSiteConfig>()
            .Where(c => c.IsDeleted == false && (
                c.ConfigKey == KeyCpu || c.ConfigKey == KeyMemory || c.ConfigKey == KeyDisk))
            .ToListAsync();
        double PercentOf(string key, double def)
            => rows.FirstOrDefault(r => r.ConfigKey == key)?.ConfigValue is { Length: > 0 } v
                && double.TryParse(v, out var n) && n is > 0 and <= 100 ? n : def;

        var thresholds = new ResourceThresholds(
            PercentOf(KeyCpu, DefaultCpuPercent),
            PercentOf(KeyMemory, DefaultMemoryPercent),
            PercentOf(KeyDisk, DefaultDiskPercent));
        cache.Set(CacheKey, thresholds, CacheTtl);
        return thresholds;
    }

    /// <summary>保存阈值（整体覆盖，保存即清缓存立即生效）；越界当场校验。</summary>
    public async Task SaveAsync(ResourceThresholds req)
    {
        if (req.CpuPercent is < 1 or > 100)
            throw new TaoAdmin.Domain.DomainException("CPU 阈值须在 1~100 之间", 400);
        if (req.MemoryPercent is < 1 or > 100)
            throw new TaoAdmin.Domain.DomainException("内存阈值须在 1~100 之间", 400);
        if (req.DiskPercent is < 1 or > 100)
            throw new TaoAdmin.Domain.DomainException("磁盘阈值须在 1~100 之间", 400);

        await UpsertAsync(KeyCpu, req.CpuPercent.ToString("0.#"), "资源监控：CPU 阈值（%）");
        await UpsertAsync(KeyMemory, req.MemoryPercent.ToString("0.#"), "资源监控：内存阈值（%）");
        await UpsertAsync(KeyDisk, req.DiskPercent.ToString("0.#"), "资源监控：磁盘阈值（%）");

        cache.Remove(CacheKey);
    }

    private async Task UpsertAsync(string key, string value, string remark)
    {
        var row = await db.Queryable<SysSiteConfig>().FirstAsync(c => c.ConfigKey == key && c.IsDeleted == false);
        if (row == null)
        {
            await db.Insertable(new SysSiteConfig
            {
                Id = snowflake.NextId(), ConfigKey = key, ConfigValue = value, Remark = remark
            }).ExecuteCommandAsync();
        }
        else
        {
            row.ConfigValue = value;
            row.UpdatedAt = DateTime.Now;
            await db.Updateable(row).ExecuteCommandAsync();
        }
    }
}
