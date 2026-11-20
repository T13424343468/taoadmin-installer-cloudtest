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
}
