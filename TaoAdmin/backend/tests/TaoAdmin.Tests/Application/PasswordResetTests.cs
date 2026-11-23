using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 自助找回密码（⑱，SQLite 内存库真库测试）：
/// 令牌一次性、限时、错误令牌拒、静默不泄露、重置后旧密码失效新密码可验、密码策略拦截、重置后吊销旧会话。
/// 邮件发送用占位 IEmailSender（记录调用，不发真信）。
/// </summary>
public class PasswordResetTests : IDisposable
{
    private readonly SqlSugarClient _db;
    private readonly OnlineSessionStore _sessions = new();
    private readonly StubEmailSender _email = new();
    private readonly StubPolicyProvider _policy = new();
    private readonly PasswordResetAppService _svc;

    public PasswordResetTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysUser, SysPasswordResetToken>();
        _svc = new PasswordResetAppService(_db, _email, _policy, _sessions);
    }

    public void Dispose() => _db.Dispose();

    private class StubEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent = new();
        public Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body)
        {
            Sent.Add((to, subject, body));
            return Task.FromResult((true, "ok"));
        }
    }

    private class StubPolicyProvider : IPasswordPolicyProvider
    {
        public PasswordPolicy Policy { get; set; } = new(8, 3, 0);
        public Task<PasswordPolicy> GetAsync() => Task.FromResult(Policy);
    }

    private long _seq = 1000;

    private SysUser NewUser(string name = "alice", string? email = "alice@test.com", string password = "OldPass@123")
        => new()
        {
            Id = ++_seq,
            UserName = name,
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(password),
            Status = 1
        };

    [Fact]
    public async Task RequestReset_UnknownUser_ShouldStaySilent()
    {
        await _svc.RequestResetAsync("ghost");

        // 实效断言：不落令牌、不发邮件（不泄露账号存在性）
        Assert.Empty(_email.Sent);
        Assert.Equal(0, _db.Queryable<SysPasswordResetToken>().Count());
    }

    [Fact]
    public async Task RequestReset_UserWithoutEmail_ShouldStaySilent()
    {
        _db.Insertable(NewUser(email: null)).ExecuteCommand();

        await _svc.RequestResetAsync("alice");

        Assert.Empty(_email.Sent);
        Assert.Equal(0, _db.Queryable<SysPasswordResetToken>().Count());
    }

    [Fact]
    public async Task RequestReset_ValidUser_ShouldEmailTokenAndStoreHashOnly()
    {
        var user = NewUser();
        _db.Insertable(user).ExecuteCommand();

        await _svc.RequestResetAsync("alice");

        // 实效断言：邮件真实发出，且正文带明文令牌；库里只存哈希不存明文
        Assert.Single(_email.Sent);
        var (to, subject, body) = _email.Sent[0];
        Assert.Equal("alice@test.com", to);
        Assert.Contains("令牌", subject);
        Assert.Matches(@"<code>([A-Za-z0-9+/=]{40,})</code>", body);

        var record = _db.Queryable<SysPasswordResetToken>().First();
        Assert.Equal("alice", record.UserName);
        Assert.Null(record.UsedAt);
        Assert.True(record.ExpireAt > DateTime.Now.AddMinutes(14), "令牌应在 15 分钟窗口内");
        Assert.DoesNotContain(record.TokenHash, body);
        Assert.Equal(64, record.TokenHash.Length); // SHA-256 hex
    }

    [Fact]
    public async Task Reset_WithWrongToken_ShouldReject()
    {
        var user = NewUser();
        _db.Insertable(user).ExecuteCommand();
        await _svc.RequestResetAsync("alice");

        await Assert.ThrowsAsync<DomainException>(() =>
            _svc.ResetAsync("alice", "dGhpcy1pcy1ub3QtdGhlLXJlYWwtdG9rZW4tYWJjZGVmZ2hpams=", "NewPass@123"));
    }

    [Fact]
    public async Task Reset_TokenIsOneTime_ShouldRejectSecondUse()
    {
        var user = NewUser();
        _db.Insertable(user).ExecuteCommand();
        await _svc.RequestResetAsync("alice");
        var raw = ExtractToken(_email.Sent[0].Body);

        await _svc.ResetAsync("alice", raw, "NewPass@123"); // 第一次成功

        // 实效断言：同一令牌第二次用必须失效
        await Assert.ThrowsAsync<DomainException>(() =>
            _svc.ResetAsync("alice", raw, "NewPass@456"));
    }

    [Fact]
    public async Task Reset_ExpiredToken_ShouldReject()
    {
        var user = NewUser();
        _db.Insertable(user).ExecuteCommand();
        await _svc.RequestResetAsync("alice");
        var raw = ExtractToken(_email.Sent[0].Body);

        // 直接把库里的令牌改成已过期（模拟超时）
        var record = _db.Queryable<SysPasswordResetToken>().First();
        record.ExpireAt = DateTime.Now.AddMinutes(-1);
        _db.Updateable(record).ExecuteCommand();

        await Assert.ThrowsAsync<DomainException>(() =>
            _svc.ResetAsync("alice", raw, "NewPass@123"));
    }

    [Fact]
    public async Task Reset_WeakPassword_ShouldBeBlockedByPolicy()
    {
        var user = NewUser();
        _db.Insertable(user).ExecuteCommand();
        await _svc.RequestResetAsync("alice");
        var raw = ExtractToken(_email.Sent[0].Body);

        // 策略 8 位 3 类：弱口令必须被同一套规则拦下
        await Assert.ThrowsAsync<DomainException>(() =>
            _svc.ResetAsync("alice", raw, "12345678"));
        // 令牌未被弱口令尝试消耗
        var record = _db.Queryable<SysPasswordResetToken>().First();
        Assert.Null(record.UsedAt);
    }

    [Fact]
    public async Task Reset_Success_ShouldInvalidateOldPasswordAndRevokeSessions()
    {
        var user = NewUser(password: "OldPass@123");
        _db.Insertable(user).ExecuteCommand();
        await _svc.RequestResetAsync("alice");
        var raw = ExtractToken(_email.Sent[0].Body);

        // 登记一个在线会话，验证重置后确实被吊销
        var session = new OnlineSession { SessionId = "s1", UserId = user.Id, UserName = "alice" };
        _sessions.Register(session);
        Assert.NotNull(_sessions.Find("s1"));

        await _svc.ResetAsync("alice", raw, "NewPass@123");

        // 实效断言：旧密码失效、新密码可验、会话被吊销、令牌标记已用
        var fresh = _db.Queryable<SysUser>().First(u => u.UserName == "alice");
        Assert.False(BCrypt.Net.BCrypt.Verify("OldPass@123", fresh.Password));
        Assert.True(BCrypt.Net.BCrypt.Verify("NewPass@123", fresh.Password));
        Assert.NotNull(fresh.PasswordUpdatedAt);
        Assert.Null(_sessions.Find("s1")); // 旧会话已强制下线
        Assert.Equal(1, _db.Queryable<SysPasswordResetToken>().First().UsedAt != null ? 1 : 0);
    }

    private static string ExtractToken(string body)
    {
        var start = body.IndexOf("<code>") + "<code>".Length;
        var end = body.IndexOf("</code>", start);
        return body[start..end];
    }
}
