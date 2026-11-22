using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Http;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>用户应用服务：分页、保存、删除、重置密码、分配角色。</summary>
public class UserAppService
{
    private readonly ISqlSugarClient _db;
    private readonly ISysUserRepository _users;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;
    private readonly Common.Messaging.IMessageQueue _mq;
    private readonly IPasswordPolicyProvider _policy;

    public UserAppService(ISqlSugarClient db, ISysUserRepository users,
        Common.Snowflake.SnowflakeIdGenerator snowflake, Common.Messaging.IMessageQueue mq,
        IPasswordPolicyProvider policy)
    {
        _db = db; _users = users; _snowflake = snowflake; _mq = mq; _policy = policy;
    }

    /// <summary>分页查询用户（聚合角色ID与角色名）</summary>
    public async Task<PageResult<UserItemDto>> PageAsync(int pageIndex, int pageSize, string? keyword)
    {
        var total = await _db.Queryable<SysUser>().CountAsync(u => u.IsDeleted == false);
        var pageUsers = await _db.Queryable<SysUser>()
            .Where(u => u.IsDeleted == false)
            .WhereIF(!string.IsNullOrEmpty(keyword), u => u.UserName.Contains(keyword!))
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var ids = pageUsers.Select(u => u.Id).ToList();
        // 一次取全关联，内存聚合角色，避免多角色分页膨胀
        var roleRows = await _db.Queryable<SysUserRole>()
            .InnerJoin<SysRole>((ur, r) => ur.RoleId == r.Id && r.IsDeleted == false)
            .Where((ur, r) => ids.Contains(ur.UserId))
            .Select((ur, r) => new { ur.UserId, RoleId = r.Id, r.RoleName })
            .ToListAsync();

        var items = pageUsers.Select(u => new UserItemDto
        {
            id = u.Id.ToString(),
            username = u.UserName,
            nickname = u.NickName,
            email = u.Email,
            phone = u.Phone,
            status = u.Status,
            roleIds = roleRows.Where(x => x.UserId == u.Id).Select(x => x.RoleId.ToString()).ToList(),
            roleNames = roleRows.Where(x => x.UserId == u.Id).Select(x => x.RoleName).ToList(),
            createdAt = u.CreatedAt
        }).ToList();

        return new PageResult<UserItemDto>(total, pageIndex, pageSize, items);
    }

    /// <summary>新增/编辑用户（带角色分配）</summary>
    public async Task SaveAsync(UserSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.username))
            throw new DomainException("账号不能为空");

        if (string.IsNullOrEmpty(req.id))
        {
            // 新增：账号唯一 + 密码必填（密码按当前生效策略校验，与改密/导入同口径）
            if (await _users.ExistsNameAsync(req.username))
                throw new DomainException("账号已存在");
            var policyError = await _policy.CheckAsync(req.password);
            if (policyError != null)
                throw new DomainException(policyError);

            var user = new SysUser
            {
                Id = NewId(),
                UserName = req.username,
                Password = BCrypt.Net.BCrypt.HashPassword(req.password),
                PasswordUpdatedAt = DateTime.Now,
                NickName = req.nickname,
                Email = req.email,
                Phone = req.phone,
                Status = req.status
            };
            await _users.AddAsync(user);
            if (req.roleIds is { Count: > 0 })
                await _users.ReplaceRolesAsync(user.Id, req.roleIds.Select(long.Parse).ToList());

            // 消息队列演示：发布用户创建事件，欢迎消息由消费者落库（见 UserCreatedMessageHandler）
            await _mq.PublishAsync("user.created", System.Text.Json.JsonSerializer.Serialize(new
            {
                userId = user.Id, userName = user.UserName, nickname = user.NickName
            }));
        }
        else
        {
            var id = long.Parse(req.id);
            var user = await _users.FindByIdAsync(id) ?? throw new DomainException("用户不存在", 404);
            user.NickName = req.nickname;
            user.Email = req.email;
            user.Phone = req.phone;
            user.Status = req.status;
            await _users.UpdateProfileAsync(user);
            if (req.roleIds != null)
                await _users.ReplaceRolesAsync(id, req.roleIds.Select(long.Parse).ToList());
        }
    }

    /// <summary>删除用户（软删 + 清理角色关联）</summary>
    public async Task DeleteAsync(long id, long currentUserId)
    {
        if (id == currentUserId)
            throw new DomainException("不能删除当前登录账号");
        await _users.SoftDeleteAsync(id);
        await _users.DeleteRoleLinksAsync(id);
    }

    /// <summary>
    /// 批量删除用户（强一致场景：整批一个事务，任一条失败全部回滚，全成全败；跳过当前登录账号）。
    /// 依据老板 2026-11-22 21:07 令：用户删除连带清理用户角色关联，部分失败会留下带角色关联的已删用户，故必须事务。
    /// </summary>
    public async Task<int> BatchDeleteAsync(List<long> ids, long currentUserId)
    {
        var skipped = 0;
        try
        {
            var result = await _db.Ado.UseTranAsync(async () =>
            {
                foreach (var id in ids.Distinct())
                {
                    if (id == currentUserId) { skipped++; continue; }
                    await _users.SoftDeleteAsync(id);
                    await _users.DeleteRoleLinksAsync(id);
                }
            });
            if (!result.IsSuccess)
                throw new DomainException($"批量删除用户失败，本次操作已全部回滚：{result.ErrorMessage}");
        }
        catch (DomainException) { throw; }
        catch (Exception ex)
        {
            // UseTranAsync 回滚后异常可能原样重抛（随版本行为有异），统一转业务异常并注明已回滚
            throw new DomainException($"批量删除用户失败，本次操作已全部回滚：{ex.Message}");
        }
        return skipped;
    }

    /// <summary>
    /// 批量启用/停用用户（可容错场景：逐条执行互不影响，单条失败不中断整批，失败明细随返回汇报；跳过当前登录账号）。
    /// 依据老板 2026-11-22 21:07 令：各用户状态互相独立，无需事务。
    /// </summary>
    public async Task<(int Skipped, List<string> Failed)> BatchSetStatusAsync(List<long> ids, int status, long currentUserId)
    {
        var skipped = 0;
        var failed = new List<string>();
        foreach (var id in ids.Distinct())
        {
            if (id == currentUserId) { skipped++; continue; }
            try
            {
                var user = await _users.FindByIdAsync(id);
                if (user == null) { skipped++; continue; }
                user.Status = status;
                await _users.UpdateProfileAsync(user);
            }
            catch (Exception ex)
            {
                // 单条失败照常走完剩余条目，失败原因随响应汇报（可容错口径，非静默吞）
                failed.Add($"{id}: {ex.Message}");
            }
        }
        return (skipped, failed);
    }

    /// <summary>重置密码（密码按当前生效策略校验）</summary>
    public async Task ResetPasswordAsync(long id, string password)
    {
        var policyError = await _policy.CheckAsync(password);
        if (policyError != null)
            throw new DomainException(policyError);
        await _users.ResetPasswordAsync(id, BCrypt.Net.BCrypt.HashPassword(password));
    }

    /// <summary>分配角色（全量覆盖）</summary>
    public async Task AssignRolesAsync(long id, List<string> roleIds)
        => await _users.ReplaceRolesAsync(id, roleIds.Select(long.Parse).ToList());

    private long NewId() => _snowflake.NextId();
}
