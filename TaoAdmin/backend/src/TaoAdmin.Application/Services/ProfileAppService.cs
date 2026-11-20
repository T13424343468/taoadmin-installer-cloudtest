using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Auth;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Repositories;
using TaoAdmin.Utilities;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 个人中心应用服务：「自己管自己」——查资料、改资料、改自己密码。
/// 与用户管理（UserAppService）的边界：本服务只操作当前登录人本人，
/// 不碰状态、角色等管理员字段；密码策略与用户管理新增/重置保持同口径。
/// </summary>
public class ProfileAppService
{
    private readonly ISysUserRepository _users;
    private readonly OnlineSessionStore _sessions;
    private readonly IPasswordPolicyProvider _policy;

    public ProfileAppService(ISysUserRepository users, OnlineSessionStore sessions, IPasswordPolicyProvider policy)
    {
        _users = users; _sessions = sessions; _policy = policy;
    }

    /// <summary>我的资料</summary>
    public async Task<ProfileDto> GetAsync(long userId)
    {
        var u = await _users.FindByIdAsync(userId) ?? throw new DomainException("用户不存在", 401);
        return new ProfileDto
        {
            userId = u.Id.ToString(),
            username = u.UserName,
            nickname = u.NickName,
            avatar = u.Avatar,
            phone = u.Phone,
            email = u.Email,
            lastLoginTime = u.LastLoginTime
        };
    }

    /// <summary>改自己的资料（昵称/头像/手机/邮箱），保存即生效</summary>
    public async Task UpdateBaseAsync(long userId, ProfileSaveRequest req)
    {
        var user = await _users.FindByIdAsync(userId) ?? throw new DomainException("用户不存在", 401);
        user.NickName = req.nickname;
        user.Phone = req.phone;
        user.Email = req.email;
        user.Avatar = req.avatar;
        await _users.UpdateSelfProfileAsync(user);
    }

    /// <summary>
    /// 改自己的密码：旧密码必校验（不可绕过），新密码受密码策略约束；
    /// 改完该用户全部会话即刻失效，需凭新密码重新登录。
    /// 密码明文只存在于请求对象内，不落日志、不落库。
    /// </summary>
    public async Task ChangePasswordAsync(long userId, ChangePasswordRequest req)
    {
        // 新密码按当前生效策略校验（管理员配置即改即生效），被拒给明确文案
        var policyError = await _policy.CheckAsync(req.newPassword);
        if (policyError != null)
            throw new DomainException(policyError);

        var user = await _users.FindByIdAsync(userId) ?? throw new DomainException("用户不存在", 401);
        if (string.IsNullOrEmpty(req.oldPassword) || !BCrypt.Net.BCrypt.Verify(req.oldPassword, user.Password))
            throw new DomainException("旧密码不正确");
        if ((req.newPassword ?? "") == req.oldPassword)
            throw new DomainException("新密码不能与当前密码相同");

        await _users.ResetPasswordAsync(userId, BCrypt.Net.BCrypt.HashPassword(req.newPassword!));
        _sessions.RevokeByUser(userId);
    }
}
