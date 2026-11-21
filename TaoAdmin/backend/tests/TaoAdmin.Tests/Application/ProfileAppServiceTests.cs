using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>个人中心服务：资料自助修改、密码修改的旧密码校验与会话全端失效。</summary>
public class ProfileAppServiceTests
{
    /// <summary>内存用户仓储替身：记录更新调用，供断言（不碰数据库）</summary>
    private class FakeUserRepository : ISysUserRepository
    {
        public readonly Dictionary<long, SysUser> Users = new();
        public SysUser? LastSelfProfileUpdate;

        public Task<SysUser?> FindByIdAsync(long id)
            => Task.FromResult(Users.TryGetValue(id, out var u) ? u : null);

        public Task<SysUser?> FindByNameAsync(string userName)
            => Task.FromResult(Users.Values.FirstOrDefault(u => u.UserName == userName));

        public Task<bool> ExistsNameAsync(string userName)
            => Task.FromResult(Users.Values.Any(u => u.UserName == userName));

        public Task AddAsync(SysUser user)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task UpdateProfileAsync(SysUser user)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task UpdateSelfProfileAsync(SysUser user)
        {
            Users[user.Id] = user;
            LastSelfProfileUpdate = user;
            return Task.CompletedTask;
        }

        public Task SoftDeleteAsync(long id)
        {
            Users.Remove(id);
            return Task.CompletedTask;
        }

        public Task TouchLoginAsync(long id) => Task.CompletedTask;

        public Task ResetPasswordAsync(long id, string passwordHash)
        {
            Users[id].Password = passwordHash;
            return Task.CompletedTask;
        }

        public Task ReplaceRolesAsync(long userId, IReadOnlyCollection<long> roleIds) => Task.CompletedTask;
        public Task DeleteRoleLinksAsync(long userId) => Task.CompletedTask;
    }

    private static SysUser NewUser(string password = "Old@123456") => new()
    {
        Id = 1, UserName = "tao", Password = BCrypt.Net.BCrypt.HashPassword(password),
        NickName = "老昵称", Status = 1, TenantId = 0
    };

    private static (ProfileAppService Svc, FakeUserRepository Repo, OnlineSessionStore Sessions) Create(SysUser? user = null)
    {
        var repo = new FakeUserRepository();
        repo.Users[user?.Id ?? 1] = user ?? NewUser();
        var sessions = new OnlineSessionStore();
        return (new ProfileAppService(repo, sessions, new DefaultPolicyStub()), repo, sessions);
    }

    [Fact]
    public async Task GetAsync_ShouldReturnSelfFields_WithoutRoles()
    {
        var (svc, repo, _) = Create();
        var dto = await svc.GetAsync(1);
        Assert.Equal("tao", dto.username);
        Assert.Equal("老昵称", dto.nickname);
        Assert.Equal("1", dto.userId);
    }

    [Fact]
    public async Task UpdateBaseAsync_ShouldPersistSelfFields_AndNotTouchStatus()
    {
        var (svc, repo, _) = Create();
        var req = new ProfileSaveRequest { nickname = "新昵称", avatar = "/uploads/a.png", phone = "138", email = "a@b.c" };
        await svc.UpdateBaseAsync(1, req);

        var saved = repo.LastSelfProfileUpdate!;
        Assert.Equal("新昵称", saved.NickName);
        Assert.Equal("/uploads/a.png", saved.Avatar);
        Assert.Equal(1, saved.Status); // 管理字段不被自助通道触碰
    }

    [Fact]
    public async Task ChangePasswordAsync_WrongOldPassword_ShouldThrow_AndKeepOld()
    {
        var (svc, repo, sessions) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.ChangePasswordAsync(1, new ChangePasswordRequest { oldPassword = "wrong", newPassword = "New@123456" }));
        Assert.True(BCrypt.Net.BCrypt.Verify("Old@123456", repo.Users[1].Password));
    }

    [Fact]
    public async Task ChangePasswordAsync_ShortPassword_ShouldThrow()
    {
        var (svc, _, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.ChangePasswordAsync(1, new ChangePasswordRequest { oldPassword = "Old@123456", newPassword = "123" }));
    }

    [Fact]
    public async Task ChangePasswordAsync_CorrectOld_ShouldUpdate_AndRevokeAllSessions()
    {
        var (svc, repo, sessions) = Create();
        sessions.Register(new OnlineSession { SessionId = "a1", UserId = 1, UserName = "tao" });
        sessions.Register(new OnlineSession { SessionId = "a2", UserId = 1, UserName = "tao" });
        sessions.Register(new OnlineSession { SessionId = "b1", UserId = 2, UserName = "lee" });

        await svc.ChangePasswordAsync(1, new ChangePasswordRequest { oldPassword = "Old@123456", newPassword = "New@123456" });

        Assert.True(BCrypt.Net.BCrypt.Verify("New@123456", repo.Users[1].Password));
        Assert.Empty(sessions.List().Where(s => s.UserId == 1)); // 该用户全部会话即刻下线
        Assert.True(sessions.IsRevoked("a1"));
        Assert.True(sessions.IsRevoked("a2"));
        Assert.False(sessions.IsRevoked("b1")); // 他人会话不受牵连
    }

    [Fact]
    public async Task ChangePasswordAsync_MissingUser_ShouldThrow()
    {
        var (svc, _, _) = Create(user: null);
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.ChangePasswordAsync(999, new ChangePasswordRequest { oldPassword = "x", newPassword = "New@123456" }));
    }
}
