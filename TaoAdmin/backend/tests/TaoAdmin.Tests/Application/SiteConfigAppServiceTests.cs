using Microsoft.Extensions.Caching.Memory;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 站点参数应用服务（SQLite 内存库真库测试）：
/// 默认值回退、保存即生效（缓存清除）、重复保存不产生重复行、空白站点名校验。
/// </summary>
public class SiteConfigAppServiceTests
{
    private static (SiteConfigAppService Svc, SqlSugarClient Db) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        var svc = new SiteConfigAppService(
            db, new SnowflakeIdGenerator(1, 1), new MemoryCache(new MemoryCacheOptions()));
        return (svc, db);
    }

    [Fact]
    public async Task GetAsync_EmptyTable_ShouldReturnBuiltInDefaults()
    {
        var (svc, _) = Create();
        var dto = await svc.GetAsync();
        Assert.Equal(SiteConfigAppService.DefaultSiteName, dto.siteName);
        Assert.Equal("", dto.siteLogo);
        Assert.Equal(SiteConfigAppService.DefaultSiteFooter, dto.siteFooter);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_ShouldReturnSavedValues_WithoutRestart()
    {
        var (svc, _) = Create();
        await svc.GetAsync(); // 预热缓存：验证保存后缓存被清除、读取立即命中新值
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "道系后台", siteLogo = "/files/logo.png", siteFooter = "TaoAdmin ©2026" });
        var dto = await svc.GetAsync();
        Assert.Equal("道系后台", dto.siteName);
        Assert.Equal("/files/logo.png", dto.siteLogo);
        Assert.Equal("TaoAdmin ©2026", dto.siteFooter);
    }

    [Fact]
    public async Task SaveAsync_Twice_ShouldUpdateInPlace_NotDuplicateRows()
    {
        var (svc, db) = Create();
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "第一版" });
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "第二版" });
        var rows = await db.Queryable<SysSiteConfig>().Where(c => c.ConfigKey == SiteConfigAppService.KeySiteName).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("第二版", row.ConfigValue);
        var dto = await svc.GetAsync();
        Assert.Equal("第二版", dto.siteName);
    }

    [Fact]
    public async Task SaveAsync_BlankSiteName_ShouldThrow()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new SiteConfigSaveRequest { siteName = "   " }));
    }

    [Fact]
    public async Task SaveAsync_NullLogoFooter_ShouldFallBackToDefaults()
    {
        var (svc, _) = Create();
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "TaoAdmin", siteLogo = null, siteFooter = null });
        var dto = await svc.GetAsync();
        Assert.Equal("", dto.siteLogo);      // 未配置 Logo 前台用默认图标
        Assert.Equal(SiteConfigAppService.DefaultSiteFooter, dto.siteFooter);
    }

    // ==================== 上传体积上限（⑬） ====================

    [Fact]
    public async Task GetAsync_未配置上传上限_返回默认50()
    {
        var (svc, _) = Create();
        var dto = await svc.GetAsync();
        Assert.Equal(SiteConfigAppService.DefaultUploadMaxSizeMB, dto.uploadMaxSizeMB);
        Assert.Equal(50, dto.uploadMaxSizeMB);
    }

    [Fact]
    public async Task SaveAsync_上传上限保存后读取生效()
    {
        var (svc, _) = Create();
        await svc.GetAsync(); // 预热缓存：验证保存后缓存被清除、读取立即命中新值
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "TaoAdmin", uploadMaxSizeMB = 100 });
        var dto = await svc.GetAsync();
        Assert.Equal(100, dto.uploadMaxSizeMB);
    }

    [Fact]
    public async Task SaveAsync_上传上限越界下界0_抛错()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new SiteConfigSaveRequest { siteName = "TaoAdmin", uploadMaxSizeMB = 0 }));
    }

    [Fact]
    public async Task SaveAsync_上传上限越界上界2049_抛错()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveAsync(new SiteConfigSaveRequest { siteName = "TaoAdmin", uploadMaxSizeMB = 2049 }));
    }

    [Fact]
    public async Task SaveAsync_上传上限边界值1与2048_保存成功()
    {
        var (svc, _) = Create();
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "TaoAdmin", uploadMaxSizeMB = 1 });
        Assert.Equal(1, (await svc.GetAsync()).uploadMaxSizeMB);
        await svc.SaveAsync(new SiteConfigSaveRequest { siteName = "TaoAdmin", uploadMaxSizeMB = 2048 });
        Assert.Equal(2048, (await svc.GetAsync()).uploadMaxSizeMB);
    }

    [Fact]
    public async Task GetAsync_缓存命中_第二次读取直接返回缓存()
    {
        var (svc, db) = Create();
        var first = await svc.GetAsync();
        // 直接改库：若第二次读取落库应看到新值，命中缓存则仍是旧值
        await db.Insertable(new SysSiteConfig
        { Id = 1, ConfigKey = SiteConfigAppService.KeySiteName, ConfigValue = "库里新名字" }).ExecuteCommandAsync();
        var second = await svc.GetAsync();
        Assert.Equal(first.siteName, second.siteName);
        Assert.Equal(SiteConfigAppService.DefaultSiteName, second.siteName);
    }

    [Fact]
    public async Task GetAsync_库中非法值_回退默认50()
    {
        var (svc, db) = Create();
        await db.Insertable(new SysSiteConfig
        { Id = 1, ConfigKey = SiteConfigAppService.KeyUploadMaxSizeMB, ConfigValue = "abc" }).ExecuteCommandAsync();
        var dto = await svc.GetAsync();
        Assert.Equal(SiteConfigAppService.DefaultUploadMaxSizeMB, dto.uploadMaxSizeMB);
    }
}
