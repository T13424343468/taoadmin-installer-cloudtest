using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Domain.Repositories;

/// <summary>用户聚合仓储抽象（写侧）；查询走应用层 CQRS 读通道</summary>
public interface ISysUserRepository
{
    Task<SysUser?> FindByIdAsync(long id);
    Task<SysUser?> FindByNameAsync(string userName);
    Task<bool> ExistsNameAsync(string userName);
    Task AddAsync(SysUser user);
    Task UpdateProfileAsync(SysUser user);
    /// <summary>更新个人自助资料（昵称/头像/手机/邮箱），不触碰状态等管理字段</summary>
    Task UpdateSelfProfileAsync(SysUser user);
    Task SoftDeleteAsync(long id);
    Task TouchLoginAsync(long id);
    Task ResetPasswordAsync(long id, string passwordHash);
    /// <summary>全量覆盖用户-角色关联</summary>
    Task ReplaceRolesAsync(long userId, IReadOnlyCollection<long> roleIds);
    Task DeleteRoleLinksAsync(long userId);
}
