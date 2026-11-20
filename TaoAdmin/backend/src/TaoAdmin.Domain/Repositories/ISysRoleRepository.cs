using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Domain.Repositories;

/// <summary>角色聚合仓储抽象（写侧）</summary>
public interface ISysRoleRepository
{
    Task<SysRole?> FindByIdAsync(long id);
    Task<bool> ExistsCodeAsync(string roleCode);
    Task AddAsync(SysRole role);
    Task UpdateAsync(SysRole role);
    /// <summary>软删角色并清理全部关联（角色-菜单/接口/用户）</summary>
    Task SoftDeleteCascadeAsync(long id);
    /// <summary>重建角色的菜单/接口关联；入参 null 表示保持不变</summary>
    Task ReplacePermissionsAsync(long roleId, IReadOnlyCollection<long>? menuIds, IReadOnlyCollection<long>? apiIds);
}
