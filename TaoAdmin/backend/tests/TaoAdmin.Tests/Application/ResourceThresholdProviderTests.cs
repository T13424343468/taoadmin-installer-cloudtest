using Microsoft.Extensions.Caching.Memory;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Monitoring;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 资源监控阈值提供口（SQLite 内存库真库测试）：
/// 未配置返回默认值、保存即生效（清缓存）、越界校验、库中越界/非法值回退默认。
/// </summary>
public class ResourceThresholdProviderTests
{
    private static (ResourceThresholdProvider Svc, SqlSugarClient Db) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        var svc = new ResourceThresholdProvider(
            db, new SnowflakeIdGenerator(1, 1), new MemoryCache(new MemoryCacheOptions()));
        return (svc, db);
    }

    [Fact]
    public async Task GetAsync_未配置_返回内置默认值()
    {
        var (svc, _) = Create();
        var thresholds = await svc.GetAsync();
        Assert.Equal(ResourceThresholdProvider.DefaultCpuPercent, thresholds.CpuPercent);
        Assert.Equal(ResourceThresholdProvider.DefaultMemoryPercent, thresholds.MemoryPercent);
        Assert.Equal(ResourceThresholdProvider.DefaultDiskPercent, thresholds.DiskPercent);
    }

    [Fact]
    public async Task SaveAsync_保存后读取生效_无需重启()
    {
        var (svc, _) = Create();
        await svc.GetAsync(); // 预热缓存：验证保存后缓存被清除
        await svc.SaveAsync(new ResourceThresholds(70, 75, 88));
        var thresholds = await svc.GetAsync();
        Assert.Equal(70, thresholds.CpuPercent);
        Assert.Equal(75, thresholds.MemoryPercent);
        Assert.Equal(88, thresholds.DiskPercent);
    }

    [Fact]
    public async Task SaveAsync_重复保存_不产生重复行()
    {
        var (svc, db) = Create();
        await svc.SaveAsync(new ResourceThresholds(70, 75, 88));
        await svc.SaveAsync(new ResourceThresholds(71, 76, 89));
        var rows = await db.Queryable<SysSiteConfig>()
            .Where(c => c.ConfigKey == ResourceThresholdProvider.KeyCpu).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("71", row.ConfigValue);
    }

    [Fact]
    public async Task SaveAsync_CPU阈值越界下界_抛错()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new ResourceThresholds(0, 85, 90)));
    }

    [Fact]
    public async Task SaveAsync_内存阈值越界上界_抛错()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new ResourceThresholds(80, 101, 90)));
    }

    [Fact]
    public async Task SaveAsync_磁盘阈值越界下界_抛错()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new ResourceThresholds(80, 85, 0)));
    }

    [Fact]
    public async Task GetAsync_缓存命中_第二次读取直接返回缓存()
    {
        var (svc, db) = Create();
        var first = await svc.GetAsync();
        // 直接改库：若第二次读取落库应看到新值，命中缓存则仍是旧值
        await db.Insertable(new SysSiteConfig
        { Id = 1, ConfigKey = ResourceThresholdProvider.KeyCpu, ConfigValue = "60" }).ExecuteCommandAsync();
        var second = await svc.GetAsync();
        Assert.Equal(first.CpuPercent, second.CpuPercent);
        Assert.Equal(ResourceThresholdProvider.DefaultCpuPercent, second.CpuPercent);
    }

    [Fact]
    public async Task GetAsync_库中越界值_回退默认()
    {
        var (svc, db) = Create();
        await db.Insertable(new SysSiteConfig
        { Id = 1, ConfigKey = ResourceThresholdProvider.KeyCpu, ConfigValue = "150" }).ExecuteCommandAsync();
        var thresholds = await svc.GetAsync();
        Assert.Equal(ResourceThresholdProvider.DefaultCpuPercent, thresholds.CpuPercent);
    }

    [Fact]
    public async Task GetAsync_库中非法值_回退默认()
    {
        var (svc, db) = Create();
        await db.Insertable(new SysSiteConfig
        { Id = 1, ConfigKey = ResourceThresholdProvider.KeyMemory, ConfigValue = "abc" }).ExecuteCommandAsync();
        var thresholds = await svc.GetAsync();
        Assert.Equal(ResourceThresholdProvider.DefaultMemoryPercent, thresholds.MemoryPercent);
    }
}
