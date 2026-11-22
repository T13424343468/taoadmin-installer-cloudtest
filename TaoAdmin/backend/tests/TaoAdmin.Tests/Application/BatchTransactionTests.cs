using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using TaoAdmin.Infrastructure.Repositories;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 批量操作事务分级（老板 2026-11-22 21:07 令：按业务场景判断事务，不搞一刀切）：
/// 强一致场景（角色/用户/接口批量删除）整批一个事务全成全败，中途失败可验证回滚；
/// 可容错场景逐条执行、失败明细随返回汇报。
/// 回滚实证用仓储桩在第 2 条注入故障，确定性验证整批回滚，不依赖跨连接 DDL。
/// </summary>
public class BatchTransactionTests : IDisposable
{
    private readonly SqlSugarClient _db;
    private readonly RoleAppService _roleSvc;
    private readonly string _dbFile;

    public BatchTransactionTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"batchtxn_{Guid.NewGuid():N}.db");
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"DataSource={_dbFile}",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysRole, SysRoleMenu, SysRoleApi, SysUserRole, SysUser>();
        var roleRepo = new SysRoleRepository(_db, new SnowflakeIdGenerator(1, 1));
        _roleSvc = new RoleAppService(_db, roleRepo, new SnowflakeIdGenerator(1, 1));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (File.Exists(_dbFile)) File.Delete(_dbFile);
    }

    private static SysRole NewRole(long id, string name) => new()
    {
        Id = id, RoleName = name, RoleCode = name, Status = 1,
        CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
    };

    private static SysUser NewUser(long id, string name) => new()
    {
        Id = id, UserName = name, Password = BCrypt.Net.BCrypt.HashPassword("Aa@123456"),
        NickName = name, Status = 1, CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
    };

    [Fact]
    public async Task RoleBatchDelete_AllValid_ShouldSoftDeleteAllAndCascadeLinks()
    {
        _db.Insertable(new[] { NewRole(1, "r1"), NewRole(2, "r2") }).ExecuteCommand();
        _db.Insertable(new SysRoleMenu { Id = 11, RoleId = 1, MenuId = 100 }).ExecuteCommand();
        _db.Insertable(new SysRoleApi { Id = 12, RoleId = 2, ApiId = 200 }).ExecuteCommand();
        _db.Insertable(new SysUserRole { Id = 13, UserId = 9, RoleId = 1 }).ExecuteCommand();

        var deleted = await _roleSvc.BatchDeleteAsync(new List<long> { 1, 2 });

        Assert.Equal(2, deleted);
        Assert.Equal(0, _db.Queryable<SysRole>().Count(r => r.IsDeleted == false)); // 软删：行仍在，有效计数归零
        Assert.Empty(_db.Queryable<SysRoleMenu>().ToList()); // 授权关系硬删级联清空
        Assert.Empty(_db.Queryable<SysRoleApi>().ToList());
        Assert.Empty(_db.Queryable<SysUserRole>().ToList());
    }

    [Fact]
    public async Task UserBatchDelete_SecondItemFails_ShouldRollBackWholeBatch()
    {
        // 强一致回滚实证：第 2 条软删注入故障，第 1 条已落库的删除必须被整批撤销
        var userRepo = new FailOnSecondSoftDeleteRepository(_db);
        var userSvc = new UserAppService(_db, userRepo, new SnowflakeIdGenerator(1, 1),
            new StubMessageQueue(), new AllowAllPolicyStub());
        _db.Insertable(new[] { NewUser(1, "u1"), NewUser(2, "u2") }).ExecuteCommand();
        _db.Insertable(new SysUserRole { Id = 21, UserId = 1, RoleId = 5 }).ExecuteCommand();
        _db.Insertable(new SysUserRole { Id = 22, UserId = 2, RoleId = 5 }).ExecuteCommand();

        var ex = await Assert.ThrowsAsync<TaoAdmin.Domain.DomainException>(
            () => userSvc.BatchDeleteAsync(new List<long> { 1, 2 }, currentUserId: 0));
        Assert.Contains("回滚", ex.Message);

        // 全成全败：用户 1 的软删与角色关联清理均已撤销，用户 2 原样
        Assert.Equal(2, _db.Queryable<SysUser>().Count(u => u.IsDeleted == false));
        Assert.Equal(2, _db.Queryable<SysUserRole>().Count());
    }

    [Fact]
    public async Task RoleBatchDelete_NonexistentIds_ShouldSkipQuietly()
    {
        _db.Insertable(NewRole(1, "r1")).ExecuteCommand();

        var deleted = await _roleSvc.BatchDeleteAsync(new List<long> { 1, 999 });

        Assert.Equal(1, deleted);
        Assert.Equal(0, _db.Queryable<SysRole>().Count(r => r.IsDeleted == false));
    }

    /// <summary>故障注入桩：真实仓储落库，第 2 次 SoftDeleteAsync 抛错，模拟批量中途失败</summary>
    private sealed class FailOnSecondSoftDeleteRepository(SqlSugarClient db) : ISysUserRepository
    {
        private readonly SysUserRepository _real = new(db, new SnowflakeIdGenerator(1, 1));
        private int _softDeleteCalls;

        public Task<SysUser?> FindByIdAsync(long id) => _real.FindByIdAsync(id);
        public Task<SysUser?> FindByNameAsync(string userName) => _real.FindByNameAsync(userName);
        public Task<bool> ExistsNameAsync(string userName) => _real.ExistsNameAsync(userName);
        public Task AddAsync(SysUser user) => _real.AddAsync(user);
        public Task UpdateProfileAsync(SysUser user) => _real.UpdateProfileAsync(user);
        public Task UpdateSelfProfileAsync(SysUser user) => _real.UpdateSelfProfileAsync(user);
        public Task TouchLoginAsync(long id) => _real.TouchLoginAsync(id);
        public Task ResetPasswordAsync(long id, string passwordHash) => _real.ResetPasswordAsync(id, passwordHash);
        public Task ReplaceRolesAsync(long userId, IReadOnlyCollection<long> roleIds) => _real.ReplaceRolesAsync(userId, roleIds);
        public Task DeleteRoleLinksAsync(long userId) => _real.DeleteRoleLinksAsync(userId);

        public async Task SoftDeleteAsync(long id)
        {
            if (++_softDeleteCalls == 2)
                throw new InvalidOperationException("注入故障：第二条软删失败");
            await _real.SoftDeleteAsync(id);
        }
    }

    private sealed class StubMessageQueue : TaoAdmin.Common.Messaging.IMessageQueue
    {
        public string ProviderName => "stub";
        public Task PublishAsync(string topic, string body) => Task.CompletedTask;
        public Task SubscribeAsync(string topic, Func<TaoAdmin.Common.Messaging.MessageEnvelope, Task> onMessage, CancellationToken ct) => Task.CompletedTask;
        public Task<Dictionary<string, long>> GetBacklogAsync() => Task.FromResult(new Dictionary<string, long>());
    }

    private sealed class AllowAllPolicyStub : TaoAdmin.Application.Services.IPasswordPolicyProvider
    {
        public Task<PasswordPolicy> GetAsync() => Task.FromResult(new PasswordPolicy(8, 2, 90));
    }
}
