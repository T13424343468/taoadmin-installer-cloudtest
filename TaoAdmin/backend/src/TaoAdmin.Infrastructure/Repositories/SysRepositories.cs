using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using SqlSugar;

namespace TaoAdmin.Infrastructure.Repositories;

/// <summary>用户仓储（SqlSugar 实现）</summary>
public class SysUserRepository : ISysUserRepository
{
    private readonly ISqlSugarClient _db;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public SysUserRepository(ISqlSugarClient db, Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _snowflake = snowflake;
    }

    public async Task<SysUser?> FindByIdAsync(long id)
        => await _db.Queryable<SysUser>().FirstAsync(u => u.Id == id && u.IsDeleted == false);

    public async Task<SysUser?> FindByNameAsync(string userName)
        => await _db.Queryable<SysUser>().FirstAsync(u => u.UserName == userName && u.IsDeleted == false);

    public async Task<bool> ExistsNameAsync(string userName)
        => await _db.Queryable<SysUser>().AnyAsync(u => u.UserName == userName && u.IsDeleted == false);

    public async Task AddAsync(SysUser user)
        => await _db.Insertable(user).ExecuteCommandAsync();

    public async Task UpdateProfileAsync(SysUser user)
        => await _db.Updateable(user)
            .UpdateColumns(u => new { u.NickName, u.Phone, u.Email, u.Status, u.UpdatedAt })
            .ExecuteCommandAsync();

    public async Task UpdateSelfProfileAsync(SysUser user)
        => await _db.Updateable(user)
            .UpdateColumns(u => new { u.NickName, u.Phone, u.Email, u.Avatar, u.UpdatedAt })
            .ExecuteCommandAsync();

    public async Task SoftDeleteAsync(long id)
        => await _db.Updateable<SysUser>()
            .SetColumns(u => new SysUser { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(u => u.Id == id)
            .ExecuteCommandAsync();

    public async Task TouchLoginAsync(long id)
        => await _db.Updateable<SysUser>()
            .SetColumns(u => new SysUser { LastLoginTime = DateTime.Now })
            .Where(u => u.Id == id)
            .ExecuteCommandAsync();

    public async Task ResetPasswordAsync(long id, string passwordHash)
        => await _db.Updateable<SysUser>()
            .SetColumns(u => new SysUser { Password = passwordHash, PasswordUpdatedAt = DateTime.Now, UpdatedAt = DateTime.Now })
            .Where(u => u.Id == id)
            .ExecuteCommandAsync();

    public async Task ReplaceRolesAsync(long userId, IReadOnlyCollection<long> roleIds)
    {
        await _db.Deleteable<SysUserRole>().Where(ur => ur.UserId == userId).ExecuteCommandAsync();
        if (roleIds.Count == 0) return;
        await _db.Insertable(roleIds.Select(roleId => new SysUserRole
        {
            Id = _snowflake.NextId(),
            UserId = userId,
            RoleId = roleId
        }).ToList()).ExecuteCommandAsync();
    }

    public async Task DeleteRoleLinksAsync(long userId)
        => await _db.Deleteable<SysUserRole>().Where(ur => ur.UserId == userId).ExecuteCommandAsync();
}

/// <summary>角色仓储（SqlSugar 实现）</summary>
public class SysRoleRepository : ISysRoleRepository
{
    private readonly ISqlSugarClient _db;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public SysRoleRepository(ISqlSugarClient db, Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _snowflake = snowflake;
    }

    public async Task<SysRole?> FindByIdAsync(long id)
        => await _db.Queryable<SysRole>().FirstAsync(r => r.Id == id && r.IsDeleted == false);

    public async Task<bool> ExistsCodeAsync(string roleCode)
        => await _db.Queryable<SysRole>().AnyAsync(r => r.RoleCode == roleCode && r.IsDeleted == false);

    public async Task AddAsync(SysRole role)
        => await _db.Insertable(role).ExecuteCommandAsync();

    public async Task UpdateAsync(SysRole role)
        => await _db.Updateable(role)
            .UpdateColumns(r => new { r.RoleName, r.RoleCode, r.Status, r.Remark, r.UpdatedAt })
            .ExecuteCommandAsync();

    public async Task SoftDeleteCascadeAsync(long id)
    {
        await _db.Updateable<SysRole>()
            .SetColumns(r => new SysRole { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(r => r.Id == id)
            .ExecuteCommandAsync();
        await _db.Deleteable<SysRoleMenu>().Where(rm => rm.RoleId == id).ExecuteCommandAsync();
        await _db.Deleteable<SysRoleApi>().Where(ra => ra.RoleId == id).ExecuteCommandAsync();
        await _db.Deleteable<SysUserRole>().Where(ur => ur.RoleId == id).ExecuteCommandAsync();
    }

    public async Task ReplacePermissionsAsync(long roleId, IReadOnlyCollection<long>? menuIds, IReadOnlyCollection<long>? apiIds)
    {
        if (menuIds != null)
        {
            await _db.Deleteable<SysRoleMenu>().Where(rm => rm.RoleId == roleId).ExecuteCommandAsync();
            if (menuIds.Count > 0)
                await _db.Insertable(menuIds.Select(menuId => new SysRoleMenu
                {
                    Id = _snowflake.NextId(), RoleId = roleId, MenuId = menuId
                }).ToList()).ExecuteCommandAsync();
        }
        if (apiIds != null)
        {
            await _db.Deleteable<SysRoleApi>().Where(ra => ra.RoleId == roleId).ExecuteCommandAsync();
            if (apiIds.Count > 0)
                await _db.Insertable(apiIds.Select(apiId => new SysRoleApi
                {
                    Id = _snowflake.NextId(), RoleId = roleId, ApiId = apiId
                }).ToList()).ExecuteCommandAsync();
        }
    }
}

/// <summary>菜单仓储（SqlSugar 实现）</summary>
public class SysMenuRepository : ISysMenuRepository
{
    private readonly ISqlSugarClient _db;

    public SysMenuRepository(ISqlSugarClient db) => _db = db;

    public async Task AddAsync(SysMenu menu)
        => await _db.Insertable(menu).ExecuteCommandAsync();

    public async Task UpdateAsync(SysMenu menu)
        => await _db.Updateable(menu)
            .UpdateColumns(m => new { m.ParentId, m.MenuName, m.MenuType, m.Path, m.Component, m.PermsCode, m.Icon, m.OrderNum, m.Visible, m.Status, m.UpdatedAt })
            .ExecuteCommandAsync();

    public async Task<int> SoftDeleteCascadeAsync(long id)
    {
        // 递归收集全部子节点
        var all = await _db.Queryable<SysMenu>().Where(m => m.IsDeleted == false).ToListAsync();
        var ids = new List<long>();
        void Collect(long parentId)
        {
            ids.Add(parentId);
            foreach (var child in all.Where(m => m.ParentId == parentId))
                Collect(child.Id);
        }
        Collect(id);

        await _db.Updateable<SysMenu>()
            .SetColumns(m => new SysMenu { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(m => ids.Contains(m.Id))
            .ExecuteCommandAsync();
        await _db.Deleteable<SysRoleMenu>().Where(rm => ids.Contains(rm.MenuId)).ExecuteCommandAsync();
        return ids.Count;
    }
}

/// <summary>接口资源仓储（SqlSugar 实现）</summary>
public class SysApiRepository : ISysApiRepository
{
    private readonly ISqlSugarClient _db;

    public SysApiRepository(ISqlSugarClient db) => _db = db;

    public async Task<bool> ExistsAsync(string path, string httpMethod)
        => await _db.Queryable<SysApi>()
            .AnyAsync(a => a.Path == path && a.HttpMethod == httpMethod && a.IsDeleted == false);

    public async Task AddAsync(SysApi api)
        => await _db.Insertable(api).ExecuteCommandAsync();

    public async Task UpdateAsync(SysApi api)
        => await _db.Updateable(api)
            .UpdateColumns(a => new { a.Name, a.Path, a.HttpMethod, a.PermsCode, a.Status, a.UpdatedAt })
            .ExecuteCommandAsync();

    public async Task SoftDeleteAsync(long id)
    {
        await _db.Updateable<SysApi>()
            .SetColumns(a => new SysApi { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(a => a.Id == id)
            .ExecuteCommandAsync();
        await _db.Deleteable<SysRoleApi>().Where(ra => ra.ApiId == id).ExecuteCommandAsync();
    }
}
