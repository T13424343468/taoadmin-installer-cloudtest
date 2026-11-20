using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Auth;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>权限查询服务（读侧）：汇总角色、权限码与菜单树。</summary>
public class PermissionAppService
{
    private readonly ISqlSugarClient _db;

    public PermissionAppService(ISqlSugarClient db) => _db = db;

    /// <summary>获取用户完整权限上下文（角色码、权限码集合、菜单树）</summary>
    public async Task<(List<string> Roles, List<string> Permissions, List<MenuNodeDto> Menus)> GetUserAuthAsync(long userId)
    {
        var roles = await _db.Queryable<SysRole>()
            .InnerJoin<SysUserRole>((r, ur) => r.Id == ur.RoleId)
            .Where((r, ur) => ur.UserId == userId && r.Status == 1 && r.IsDeleted == false)
            .ToListAsync();
        var roleIds = roles.Select(r => r.Id).ToList();
        var roleCodes = roles.Select(r => r.RoleCode).ToList();

        // 超级管理员：全部菜单与权限码
        if (roleCodes.Contains(Common.Auth.PermissionAuthorizationHandler.SuperAdminRole))
        {
            var allMenus = await _db.Queryable<SysMenu>().Where(m => m.Status == 1 && m.IsDeleted == false).ToListAsync();
            var allPerms = await _db.Queryable<SysApi>().Where(a => a.Status == 1 && a.IsDeleted == false)
                .Select(a => a.PermsCode).ToListAsync();
            var perms = allMenus.Where(m => !string.IsNullOrEmpty(m.PermsCode)).Select(m => m.PermsCode!)
                .Concat(allPerms).Distinct().ToList();
            return (roleCodes, perms, BuildTree(allMenus));
        }

        if (roleIds.Count == 0)
            return (roleCodes, new List<string>(), new List<MenuNodeDto>());

        var menus = await _db.Queryable<SysMenu>()
            .InnerJoin<SysRoleMenu>((m, rm) => m.Id == rm.MenuId)
            .Where((m, rm) => roleIds.Contains(rm.RoleId) && m.Status == 1 && m.IsDeleted == false)
            .Distinct()
            .ToListAsync();
        var apiPerms = await _db.Queryable<SysApi>()
            .InnerJoin<SysRoleApi>((a, ra) => a.Id == ra.ApiId)
            .Where((a, ra) => roleIds.Contains(ra.RoleId) && a.Status == 1 && a.IsDeleted == false)
            .Select((a, ra) => a.PermsCode)
            .ToListAsync();

        var permissions = menus.Where(m => !string.IsNullOrEmpty(m.PermsCode)).Select(m => m.PermsCode!)
            .Concat(apiPerms).Distinct().ToList();
        return (roleCodes, permissions, BuildTree(menus));
    }

    /// <summary>平铺菜单转树（仅目录/菜单进导航树，按钮挂在树中供前端取权限）</summary>
    public List<MenuNodeDto> BuildTree(List<SysMenu> menus)
    {
        var nodes = menus.Select(ToNode).ToDictionary(n => n.id);
        var roots = new List<MenuNodeDto>();
        foreach (var node in nodes.Values)
        {
            if (!string.IsNullOrEmpty(node.parentId) && node.parentId != "0" && nodes.TryGetValue(node.parentId!, out var parent))
                parent.children.Add(node);
            else
                roots.Add(node);
        }
        void Sort(List<MenuNodeDto> list)
        {
            list.Sort((a, b) => a.sort.CompareTo(b.sort));
            foreach (var item in list) Sort(item.children);
        }
        Sort(roots);
        return roots;
    }

    private static MenuNodeDto ToNode(SysMenu m) => new()
    {
        id = m.Id.ToString(),
        parentId = m.ParentId == 0 ? null : m.ParentId.ToString(),
        name = m.MenuName,
        path = m.Path,
        component = m.Component,
        icon = m.Icon,
        type = m.MenuType switch { 0 => "dir", 2 => "button", _ => "menu" },
        permission = m.PermsCode,
        visible = m.Visible,
        status = m.Status,
        sort = m.OrderNum
    };
}

/// <summary>认证应用服务：登录、当前用户、退出。</summary>
public class AuthAppService
{
    private readonly ISqlSugarClient _db;
    private readonly ISysUserRepository _users;
    private readonly JwtTokenService _jwt;
    private readonly Common.Cache.RedisHelper _redis;
    private readonly PermissionAppService _permission;
    private readonly OnlineSessionStore _sessions;
    private readonly IPasswordPolicyProvider _policy;
    private readonly ILoginSecurityProvider _security;
    private readonly LoginLockoutStore _lockout;
    private readonly Common.Security.CaptchaService _captcha;

    public AuthAppService(ISqlSugarClient db, ISysUserRepository users, JwtTokenService jwt,
        Common.Cache.RedisHelper redis, PermissionAppService permission, OnlineSessionStore sessions,
        IPasswordPolicyProvider policy, ILoginSecurityProvider security,
        LoginLockoutStore lockout, Common.Security.CaptchaService captcha)
    {
        _db = db; _users = users; _jwt = jwt; _redis = redis; _permission = permission; _sessions = sessions;
        _policy = policy; _security = security; _lockout = lockout; _captcha = captcha;
    }

    /// <summary>账号密码登录，签发令牌并返回用户上下文（登录来源供在线名单展示）。</summary>
    /// <remarks>安全链路顺序（设计定稿 L 节）：判锁先于密码校验（防继续撞库）→ 验证码按设置消费 →
    /// 密码校验（失败记账、达阈值锁定）→ 成功清零解锁。</remarks>
    public async Task<(string Token, CurrentUserDto User)> LoginAsync(LoginRequest request, string ip = "", string userAgent = "")
    {
        var security = await _security.GetAsync();

        // 1. 判锁先于一切：锁定中一律拒绝并提示剩余分钟，不计新失败
        var locked = _lockout.GetRemainingLockMinutes(request.username, security.LoginLockMinutes);
        if (locked.HasValue)
            throw new DomainException($"连续登录失败已达 {security.LoginMaxFails} 次，账号已锁定，请约 {locked.Value} 分钟后再试", 423);

        // 2. 验证码：开关开启且（每次都要 或 已失败达阈值）时必须携带且正确；错误不进锁定账（防恶意灌账本）
        var captchaNeeded = security.LoginCaptchaRequired &&
            (security.CaptchaAfterFails == 0 || _lockout.GetFails(request.username) >= security.CaptchaAfterFails);
        if (captchaNeeded && !_captcha.Verify(request.captchaId, request.captchaCode))
            throw new DomainException("验证码不正确或已过期", 400);

        var user = await _users.FindByNameAsync(request.username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.password, user.Password))
        {
            _lockout.RecordFailure(request.username, security.LoginMaxFails, security.LoginLockMinutes);
            throw new DomainException("账号或密码错误", 400);
        }
        if (user.Status != 1)
            throw new DomainException("账号已停用，请联系管理员", 403);

        // 3. 登录成功：清零解锁
        _lockout.Clear(request.username);

        var (roles, permissions, menus) = await _permission.GetUserAuthAsync(user.Id);
        var (token, _, sessionId) = _jwt.GenerateTokenWithSession(user.Id, user.UserName, roles, permissions, user.TenantId);
        _redis.SetString($"refresh_token:{user.Id}", _jwt.GenerateRefreshToken(), TimeSpan.FromDays(_jwt.RefreshExpireDays));
        await _users.TouchLoginAsync(user.Id);

        // 登记在线会话：在线用户名单与强制下线的账本
        _sessions.Register(new OnlineSession
        {
            SessionId = sessionId,
            UserId = user.Id,
            UserName = user.UserName,
            NickName = user.NickName,
            Ip = ip,
            UserAgent = userAgent
        });

        var tenantName = user.TenantId == 0 ? "平台" : (await _db.Queryable<Domain.Entities.SysTenant>()
            .FirstAsync(t => t.Id == user.TenantId))?.TenantName;
        var policy = await _policy.GetAsync();
        var mustChange = PasswordPolicyEx.IsExpired(policy, user.PasswordUpdatedAt, user.CreatedAt, DateTime.Now);

        return (token, new CurrentUserDto
        {
            userId = user.Id.ToString(),
            username = user.UserName,
            nickname = user.NickName,
            roles = roles,
            permissions = permissions,
            menus = menus,
            tenantId = user.TenantId.ToString(),
            tenantName = tenantName ?? "平台",
            mustChangePassword = mustChange
        });
    }

    /// <summary>当前登录用户信息（角色 + 权限码 + 菜单树）</summary>
    public async Task<CurrentUserDto> GetCurrentUserAsync(long userId)
    {
        var user = await _users.FindByIdAsync(userId) ?? throw new DomainException("用户不存在", 401);
        var (roles, permissions, menus) = await _permission.GetUserAuthAsync(userId);
        var tenantName = user.TenantId == 0 ? "平台" : (await _db.Queryable<Domain.Entities.SysTenant>()
            .FirstAsync(t => t.Id == user.TenantId))?.TenantName;
        var policy = await _policy.GetAsync();
        return new CurrentUserDto
        {
            userId = user.Id.ToString(),
            username = user.UserName,
            nickname = user.NickName,
            avatar = user.Avatar,
            roles = roles,
            permissions = permissions,
            menus = menus,
            tenantId = user.TenantId.ToString(),
            tenantName = tenantName ?? "平台",
            mustChangePassword = PasswordPolicyEx.IsExpired(policy, user.PasswordUpdatedAt, user.CreatedAt, DateTime.Now)
        };
    }

    /// <summary>退出登录（无状态 JWT，清理刷新令牌并吊销当前会话）</summary>
    public async Task LogoutAsync(long userId, string? sessionId = null)
    {
        if (!string.IsNullOrEmpty(sessionId))
            _sessions.Revoke(sessionId);
        await Task.Run(() => _redis.Del($"refresh_token:{userId}"));
    }
}
