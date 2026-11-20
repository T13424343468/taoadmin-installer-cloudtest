using SqlSugar;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using TaoAdmin.Utilities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 密码策略测试桩：策略可配置（默认=内置默认策略），CheckAsync 走接口默认实现（与真库无差）。
/// </summary>
internal class DefaultPolicyStub : IPasswordPolicyProvider
{
    public PasswordPolicy Policy { get; set; } = new(
        PasswordPolicyAppService.DefaultMinLength,
        PasswordPolicyAppService.DefaultMinKinds,
        PasswordPolicyAppService.DefaultForceChangeDays);

    public Task<PasswordPolicy> GetAsync() => Task.FromResult(Policy);
}

/// <summary>
/// 密码策略 T5-01~T5-10：
/// 纯函数校验、强制改密判定分支、四处生效点（改密/建人/重置/导入）按策略拒收与放行、
/// 策略服务默认值/边界校验/保存即生效。
/// </summary>
public class PasswordPolicyTests
{
    // ---------------- T5-01/T5-02 CheckPassword 纯函数 ----------------

    [Fact]
    public void CheckPassword_NullOrEmpty_ShouldFailByLength()
    {
        Assert.Equal("密码长度不能少于 8 位", ValidateHelper.CheckPassword(null, 8, 3));
        Assert.Equal("密码长度不能少于 8 位", ValidateHelper.CheckPassword("", 8, 3));
    }

    [Fact]
    public void CheckPassword_TooShort_ShouldReturnLengthMessage()
    {
        Assert.Equal("密码长度不能少于 8 位", ValidateHelper.CheckPassword("Aa@123", 8, 3));
        // 自定义更严策略：12 位
        Assert.Equal("密码长度不能少于 12 位", ValidateHelper.CheckPassword("Aa@123456", 12, 3));
    }

    [Fact]
    public void CheckPassword_TooFewKinds_ShouldReturnKindMessage()
    {
        // 8 位但只有 2 类（小写+数字）
        Assert.Equal("密码须包含大小写字母、数字、特殊字符中至少 3 类",
            ValidateHelper.CheckPassword("abcdefgh1", 8, 3));
        // 更宽策略：只要求 1 类 → 通过
        Assert.Null(ValidateHelper.CheckPassword("abcdefgh1", 8, 1));
    }

    [Fact]
    public void CheckPassword_PassingPassword_ShouldReturnNull()
    {
        Assert.Null(ValidateHelper.CheckPassword("Aa@123456", 8, 3));
        Assert.Null(ValidateHelper.CheckPassword("New@123456", 8, 3));
    }

    [Fact]
    public void PasswordKindCount_ShouldCountFourClasses()
    {
        Assert.Equal(4, ValidateHelper.PasswordKindCount("Aa@1234"));
        Assert.Equal(3, ValidateHelper.PasswordKindCount("aa@1234"));
        Assert.Equal(2, ValidateHelper.PasswordKindCount("aa123456"));
        Assert.Equal(1, ValidateHelper.PasswordKindCount("aaaaaaaa"));
    }

    // ---------------- T5-03 IsExpired 强制改密判定分支 ----------------

    [Fact]
    public void IsExpired_ZeroDays_ShouldNeverExpire()
    {
        var policy = new PasswordPolicy(8, 3, 0);
        var longAgo = DateTime.Now.AddDays(-3650);
        Assert.False(PasswordPolicyEx.IsExpired(policy, null, longAgo, DateTime.Now));
    }

    [Fact]
    public void IsExpired_ExpiredByPasswordUpdatedAt_ShouldBeTrue()
    {
        var policy = new PasswordPolicy(8, 3, 90);
        var updated = DateTime.Now.AddDays(-91);
        Assert.True(PasswordPolicyEx.IsExpired(policy, updated, DateTime.Now.AddDays(-1), DateTime.Now));
    }

    [Fact]
    public void IsExpired_NotYetExpired_ShouldBeFalse()
    {
        var policy = new PasswordPolicy(8, 3, 90);
        var updated = DateTime.Now.AddDays(-89);
        Assert.False(PasswordPolicyEx.IsExpired(policy, updated, DateTime.Now.AddDays(-1), DateTime.Now));
    }

    [Fact]
    public void IsExpired_NullPasswordUpdatedAt_ShouldFallBackToCreatedAt()
    {
        var policy = new PasswordPolicy(8, 3, 90);
        var created = DateTime.Now.AddDays(-100);
        Assert.True(PasswordPolicyEx.IsExpired(policy, null, created, DateTime.Now));
        // 边界：基准+N 天 == now 不算过期（严格大于）
        var boundary = DateTime.Now.AddDays(-90);
        Assert.False(PasswordPolicyEx.IsExpired(policy, null, boundary, boundary.AddDays(90)));
    }

    // ---------------- T5-04/T5-05 ProfileAppService 改密按策略拒收/放行 ----------------

    [Fact]
    public async Task ChangePassword_StrictPolicy_ShouldRejectWithPolicyMessage()
    {
        var (svc, repo) = CreateProfile(policy: new PasswordPolicy(12, 3, 0));
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            svc.ChangePasswordAsync(1, new ChangePasswordRequest { oldPassword = "Old@123456", newPassword = "New@123456" }));
        Assert.Equal("密码长度不能少于 12 位", ex.Message);
        // 旧密码未被改动
        Assert.True(BCrypt.Net.BCrypt.Verify("Old@123456", repo.Users[1].Password));
    }

    [Fact]
    public async Task ChangePassword_DefaultPolicy_ShouldPass()
    {
        var (svc, repo) = CreateProfile();
        await svc.ChangePasswordAsync(1, new ChangePasswordRequest { oldPassword = "Old@123456", newPassword = "New@123456" });
        Assert.True(BCrypt.Net.BCrypt.Verify("New@123456", repo.Users[1].Password));
    }

    // ---------------- T5-06 UserAppService 重置/新增按策略（SQLite 真库） ----------------

    [Fact]
    public async Task ResetPassword_StrictPolicy_ShouldReject_AndKeepOldHash()
    {
        using var ctx = NewUserDb();
        var before = ctx.Db.Queryable<SysUser>().First(u => u.UserName == "u1").Password;
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => ctx.Svc.ResetPasswordAsync(ctx.UserId, "123"));
        Assert.Contains("密码", ex.Message);
        Assert.Equal(before, ctx.Db.Queryable<SysUser>().First(u => u.UserName == "u1").Password);
    }

    [Fact]
    public async Task ResetPassword_PassingPassword_ShouldUpdateHash()
    {
        using var ctx = NewUserDb();
        await ctx.Svc.ResetPasswordAsync(ctx.UserId, "New@123456");
        Assert.True(BCrypt.Net.BCrypt.Verify("New@123456",
            ctx.Db.Queryable<SysUser>().First(u => u.Id == ctx.UserId).Password));
    }

    [Fact]
    public async Task SaveUser_WeakPassword_ShouldRejectWithPolicyMessage()
    {
        using var ctx = NewUserDb();
        var ex = await Assert.ThrowsAsync<DomainException>(() => ctx.Svc.SaveAsync(new UserSaveRequest
        {
            username = "weak", password = "123", status = 1
        }));
        Assert.Equal("密码长度不能少于 8 位", ex.Message);
        Assert.Equal(0, ctx.Db.Queryable<SysUser>().Count(u => u.UserName == "weak"));
    }

    [Fact]
    public async Task SaveUser_PassingPassword_ShouldInsert_WithPasswordUpdatedAt()
    {
        using var ctx = NewUserDb();
        await ctx.Svc.SaveAsync(new UserSaveRequest
        {
            username = "strong", nickname = "强口令", password = "Aa@123456", status = 1
        });
        var user = ctx.Db.Queryable<SysUser>().First(u => u.UserName == "strong");
        Assert.True(BCrypt.Net.BCrypt.Verify("Aa@123456", user.Password));
        Assert.NotNull(user.PasswordUpdatedAt); // 建号即记改密基准，供强制改密判定
    }

    // ---------------- T5-08 导入按策略拒收（文案同口径） ----------------

    [Fact]
    public async Task Import_WeakPassword_ShouldReportPolicyMessage()
    {
        using var db = NewSqlite();
        db.CodeFirst.InitTables<SysUser>();
        var svc = new UserExcelAppService(db, new SnowflakeIdGenerator(1, 1), new DefaultPolicyStub
        {
            Policy = new PasswordPolicy(12, 3, 0)
        });
        var ms = await WriteImportRows(new Dictionary<string, object>
        {
            ["用户名"] = "import1", ["昵称"] = "", ["邮箱"] = "", ["手机号"] = "",
            ["状态(启用/禁用)"] = "启用", ["密码(仅导入,留空用默认密码)"] = "Aa@123456" // 不足 12 位
        });
        var result = await svc.ImportAsync(ms, 1);

        Assert.Equal(1, result.total);
        Assert.Equal(0, result.success);
        var err = Assert.Single(result.errors);
        Assert.Equal("用户名「import1」密码长度不能少于 12 位", err.reason);
        Assert.Equal(0, db.Queryable<SysUser>().Count());
    }

    [Fact]
    public async Task Import_WeakDefaultPassword_ShouldAlsoBeRejected_WhenPolicyStricter()
    {
        using var db = NewSqlite();
        db.CodeFirst.InitTables<SysUser>();
        var svc = new UserExcelAppService(db, new SnowflakeIdGenerator(1, 1), new DefaultPolicyStub
        {
            Policy = new PasswordPolicy(12, 3, 0) // 默认密码 Aa@123456 只有 9 位，更严策略下同样拦下
        });
        var ms = await WriteImportRows(new Dictionary<string, object>
        {
            ["用户名"] = "import2", ["昵称"] = "", ["邮箱"] = "", ["手机号"] = "",
            ["状态(启用/禁用)"] = "启用", ["密码(仅导入,留空用默认密码)"] = ""
        });
        var result = await svc.ImportAsync(ms, 1);
        Assert.Equal(0, result.success);
        Assert.Contains(result.errors, e => e.reason.Contains("密码长度不能少于 12 位"));
    }

    // ---------------- T5-09/T5-10 策略服务默认值/边界/保存即生效（SQLite 真库） ----------------

    [Fact]
    public async Task PolicyService_EmptyDb_ShouldReturnBuiltInDefaults()
    {
        using var db = NewSqlite();
        db.CodeFirst.InitTables<SysSiteConfig>();
        var svc = new PasswordPolicyAppService(db, new SnowflakeIdGenerator(1, 1),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var policy = await svc.GetAsync();
        Assert.Equal(PasswordPolicyAppService.DefaultMinLength, policy.MinLength);
        Assert.Equal(PasswordPolicyAppService.DefaultMinKinds, policy.MinKinds);
        Assert.Equal(PasswordPolicyAppService.DefaultForceChangeDays, policy.ForceChangeDays);
    }

    [Fact]
    public async Task PolicyService_SaveOutOfBound_ShouldThrow()
    {
        using var db = NewSqlite();
        db.CodeFirst.InitTables<SysSiteConfig>();
        var svc = new PasswordPolicyAppService(db, new SnowflakeIdGenerator(1, 1),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(new PasswordPolicy(5, 3, 0)));   // 长度下限
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(new PasswordPolicy(65, 3, 0)));  // 长度上限
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(new PasswordPolicy(8, 0, 0)));   // 类数下限
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(new PasswordPolicy(8, 5, 0)));   // 类数上限
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(new PasswordPolicy(8, 3, -1)));  // 天数下限
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveAsync(new PasswordPolicy(8, 3, 3651)));// 天数上限
    }

    [Fact]
    public async Task PolicyService_SaveThenRead_ShouldTakeEffectImmediately()
    {
        using var db = NewSqlite();
        db.CodeFirst.InitTables<SysSiteConfig>();
        var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var svc = new PasswordPolicyAppService(db, new SnowflakeIdGenerator(1, 1), cache);

        await svc.GetAsync(); // 预热缓存（旧值）
        await svc.SaveAsync(new PasswordPolicy(10, 2, 30));

        // 保存即清缓存：同一缓存实例再读是落库后的新值
        var policy = await svc.GetAsync();
        Assert.Equal(10, policy.MinLength);
        Assert.Equal(2, policy.MinKinds);
        Assert.Equal(30, policy.ForceChangeDays);
    }

    // ---------------- 公共脚手架 ----------------

    private static SqlSugarClient NewSqlite() => new(new ConnectionConfig
    {
        ConnectionString = "DataSource=:memory:",
        DbType = SqlSugar.DbType.Sqlite,
        IsAutoCloseConnection = false
    });

    private sealed class UserDbCtx(SqlSugarClient db, UserAppService svc, long userId) : IDisposable
    {
        public SqlSugarClient Db { get; } = db;
        public UserAppService Svc { get; } = svc;
        public long UserId { get; } = userId;
        public void Dispose() => Db.Dispose();
    }

    private static UserDbCtx NewUserDb()
    {
        var db = NewSqlite();
        db.CodeFirst.InitTables<SysUser, SysUserRole>();
        var userId = 1L;
        db.Insertable(new SysUser
        {
            Id = userId, UserName = "u1", Password = BCrypt.Net.BCrypt.HashPassword("Old@123456"),
            NickName = "用户一", Status = 1, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
        }).ExecuteCommand();
        // 消息队列桩：建人发布事件无需真实队列
        var svc = new UserAppService(db, new FakeUserRepository(db), new SnowflakeIdGenerator(1, 1),
            new FakeMessageQueue(), new DefaultPolicyStub());
        return new UserDbCtx(db, svc, userId);
    }

    private static async Task<MemoryStream> WriteImportRows(params Dictionary<string, object>[] rows)
    {
        var outMs = new MemoryStream();
        await MiniExcelLibs.MiniExcel.SaveAsAsync(outMs, rows.ToList(), sheetName: "用户导入模板");
        return new MemoryStream(outMs.ToArray());
    }

    /// <summary>内存用户仓储替身：改密测试不碰数据库；SQLite 场景走 NewUserDb 的直连实现</summary>
    private class FakeUserRepository(SqlSugarClient? db = null) : ISysUserRepository
    {
        public readonly Dictionary<long, SysUser> Users = new();

        public async Task<SysUser?> FindByIdAsync(long id)
            => db != null
                ? await db.Queryable<SysUser>().FirstAsync(u => u.Id == id)
                : Users.TryGetValue(id, out var u) ? u : null;

        public async Task<SysUser?> FindByNameAsync(string userName)
            => db != null
                ? await db.Queryable<SysUser>().FirstAsync(u => u.UserName == userName)
                : Users.Values.FirstOrDefault(u => u.UserName == userName);

        public async Task<bool> ExistsNameAsync(string userName)
            => db != null
                ? await db.Queryable<SysUser>().AnyAsync(u => u.UserName == userName && u.IsDeleted == false)
                : Users.Values.Any(u => u.UserName == userName);

        public Task AddAsync(SysUser user)
        {
            if (db != null) { db.Insertable(user).ExecuteCommand(); return Task.CompletedTask; }
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task UpdateProfileAsync(SysUser user)
        {
            if (db != null) { db.Updateable(user).ExecuteCommand(); return Task.CompletedTask; }
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task UpdateSelfProfileAsync(SysUser user)
        {
            if (db != null) { db.Updateable(user).ExecuteCommand(); return Task.CompletedTask; }
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(long id)
        {
            if (db != null)
            {
                db.Updateable<SysUser>().SetColumns(u => new SysUser { IsDeleted = true }).Where(u => u.Id == id).ExecuteCommand();
                return Task.CompletedTask;
            }
            Users.Remove(id);
            return Task.CompletedTask;
        }

        public Task TouchLoginAsync(long id) => Task.CompletedTask;

        public Task ResetPasswordAsync(long id, string passwordHash)
        {
            if (db != null)
            {
                db.Updateable<SysUser>()
                    .SetColumns(u => new SysUser { Password = passwordHash, PasswordUpdatedAt = DateTime.Now })
                    .Where(u => u.Id == id).ExecuteCommand();
                return Task.CompletedTask;
            }
            Users[id].Password = passwordHash;
            return Task.CompletedTask;
        }

        public Task ReplaceRolesAsync(long userId, IReadOnlyCollection<long> roleIds) => Task.CompletedTask;
        public Task DeleteRoleLinksAsync(long userId) => Task.CompletedTask;
    }

    private class FakeMessageQueue : TaoAdmin.Common.Messaging.IMessageQueue
    {
        public string ProviderName => "memory";
        public Task PublishAsync(string topic, string body) => Task.CompletedTask;
        public Task SubscribeAsync(string topic, Func<TaoAdmin.Common.Messaging.MessageEnvelope, Task> onMessage, CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, long>> GetBacklogAsync() => Task.FromResult(new Dictionary<string, long>());
    }

    private static (ProfileAppService Svc, FakeUserRepository Repo) CreateProfile(PasswordPolicy? policy = null)
    {
        var repo = new FakeUserRepository(db: null);
        repo.Users[1] = new SysUser
        {
            Id = 1, UserName = "tao", Password = BCrypt.Net.BCrypt.HashPassword("Old@123456"),
            NickName = "老昵称", Status = 1, TenantId = 0
        };
        var stub = new DefaultPolicyStub();
        if (policy != null) stub.Policy = policy;
        return (new ProfileAppService(repo, new OnlineSessionStore(), stub), repo);
    }
}
