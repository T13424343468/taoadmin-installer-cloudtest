using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Domain.Repositories;

/// <summary>菜单聚合仓储抽象（写侧）</summary>
public interface ISysMenuRepository
{
    Task AddAsync(SysMenu menu);
    Task UpdateAsync(SysMenu menu);
    /// <summary>软删并级联全部子节点，返回删除的节点数</summary>
    Task<int> SoftDeleteCascadeAsync(long id);
}
