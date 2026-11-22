using Microsoft.Extensions.Caching.Memory;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 登录安全设置·同账号互踢开关（SQLite 内存库真库测试）：
/// 默认关闭、保存开启后读取立即生效、重复保存不产生重复行。
/// 互踢的会话吊销语义由 OnlineSessionStoreTests.RevokeByUser 覆盖；登录链路接线由批次末 E2E 复核。
/// </summary>
public class LoginKickSettingTests
{
    private static (LoginSecurityAppService Svc, SqlSugarClient Db) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysSiteConfig>();
        var svc = new LoginSecurityAppService(
            db, new SnowflakeIdGenerator(1, 1), new MemoryCache(new MemoryCacheOptions()));
        return (svc, db);
    }

    [Fact]
    public async Task GetAsync_EmptyTable_KickShouldDefaultOff()
    {
        var (svc, _) = Create();
        var settings = await svc.GetAsync();
        Assert.False(settings.KickOnDuplicateLogin);
    }

    [Fact]
    public async Task SaveAsync_KickOn_ShouldTakeEffectImmediately()
    {
        var (svc, _) = Create();
        await svc.GetAsync(); // 预热缓存：验证保存后缓存被清除、读取立即命中新值
        await svc.SaveAsync(new LoginSecuritySettings(5, 15, true, 0, 90, KickOnDuplicateLogin: true));

        var settings = await svc.GetAsync();
        Assert.True(settings.KickOnDuplicateLogin);

        // 关回：开关可逆，关闭后恢复多端并存
        await svc.SaveAsync(new LoginSecuritySettings(5, 15, true, 0, 90, KickOnDuplicateLogin: false));
        Assert.False((await svc.GetAsync()).KickOnDuplicateLogin);
    }

    [Fact]
    public async Task SaveAsync_Twice_ShouldUpdateInPlace_NotDuplicateRows()
    {
        var (svc, db) = Create();
        await svc.SaveAsync(new LoginSecuritySettings(5, 15, true, 0, 90, KickOnDuplicateLogin: true));
        await svc.SaveAsync(new LoginSecuritySettings(5, 15, true, 0, 90, KickOnDuplicateLogin: false));

        var rows = await db.Queryable<SysSiteConfig>()
            .Where(c => c.ConfigKey == LoginSecurityKeys.KickOnDuplicateLogin).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal("0", row.ConfigValue);
    }
}
