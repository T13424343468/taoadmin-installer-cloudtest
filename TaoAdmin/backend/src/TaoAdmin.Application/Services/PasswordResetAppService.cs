using TaoAdmin.Domain;
using TaoAdmin.Common.Auth;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 自助找回密码（⑱）：邮箱申请重置令牌 → 凭令牌重置。
/// 令牌一次性、限时（15 分钟）、SHA-256 哈希落库；重置成功自动吊销该用户全部旧会话。
/// 申请接口不泄露账号是否存在（统一提示）。
/// </summary>
public class PasswordResetAppService(ISqlSugarClient db, IEmailSender email, IPasswordPolicyProvider policy,
    OnlineSessionStore sessions)
{
    /// <summary>令牌有效期（分钟）</summary>
    public const int TokenTtlMinutes = 15;

    /// <summary>申请重置：生成一次性令牌并发邮件；账号不存在或无邮箱时静默成功（不泄露账号存在性）</summary>
    public async Task RequestResetAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("请输入账号", 400);

        var user = await db.Queryable<SysUser>()
            .Where(u => u.UserName == userName.Trim() && u.IsDeleted == false)
            .FirstAsync();
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
            return; // 静默：不泄露账号是否存在/是否绑邮箱

        // 一次性令牌：明文走邮件，库里只存 SHA-256
        var rawToken = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var tokenHash = Hash(rawToken);

        // 作废该账号旧令牌，落新令牌
        await db.Updateable<SysPasswordResetToken>()
            .Where(t => t.UserName == user.UserName && t.UsedAt == null)
            .SetColumns(t => new SysPasswordResetToken { UsedAt = DateTime.Now })
            .ExecuteCommandAsync();

        db.Insertable(new SysPasswordResetToken
        {
            UserName = user.UserName,
            TokenHash = tokenHash,
            ExpireAt = DateTime.Now.AddMinutes(TokenTtlMinutes)
        }).ExecuteCommand();

        var body = $"<p>您（账号 {user.UserName}）申请重置密码。重置令牌（15 分钟内有效，仅可用一次）：</p>" +
                   $"<p><code>{rawToken}</code></p>" +
                   "<p>如非本人操作请忽略本邮件。</p>";
        await email.SendAsync(user.Email, "密码重置令牌", body);
    }

    /// <summary>凭令牌重置密码：校验一次性+限时，重置后吊销全部旧会话</summary>
    public async Task ResetAsync(string userName, string rawToken, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(rawToken))
            throw new DomainException("参数不完整", 400);

        var tokenHash = Hash(rawToken);
        var record = await db.Queryable<SysPasswordResetToken>()
            .Where(t => t.UserName == userName.Trim() && t.TokenHash == tokenHash)
            .FirstAsync();
        if (record == null || record.UsedAt != null || record.ExpireAt < DateTime.Now)
            throw new DomainException("重置令牌无效或已过期，请重新申请", 400);

        // 密码策略校验（与修改密码同一套规则：长度 + 字符类数）
        var reason = await policy.CheckAsync(newPassword);
        if (reason != null)
            throw new DomainException(reason, 400);

        var user = await db.Queryable<SysUser>()
            .Where(u => u.UserName == userName.Trim() && u.IsDeleted == false)
            .FirstAsync();
        if (user == null)
            throw new DomainException("账号不存在", 400);

        user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        user.PasswordUpdatedAt = DateTime.Now;
        user.UpdatedAt = DateTime.Now;
        await db.Updateable(user)
            .UpdateColumns(u => new { u.Password, u.PasswordUpdatedAt, u.UpdatedAt })
            .ExecuteCommandAsync();

        // 令牌标记已用（一次性）
        record.UsedAt = DateTime.Now;
        await db.Updateable(record).UpdateColumns(t => new { t.UsedAt }).ExecuteCommandAsync();

        // 重置成功即吊销该用户全部旧会话（改密后全端失效，与改密接口同口径）
        sessions.RevokeByUser(user.Id);
    }

    internal static string Hash(string rawToken)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));
}
