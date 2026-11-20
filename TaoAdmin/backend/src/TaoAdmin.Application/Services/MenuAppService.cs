using TaoAdmin.Application.Dtos;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>菜单应用服务：树查询、保存、级联删除。</summary>
public class MenuAppService
{
    private readonly ISqlSugarClient _db;
    private readonly ISysMenuRepository _menus;
    private readonly PermissionAppService _permission;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public MenuAppService(ISqlSugarClient db, ISysMenuRepository menus, PermissionAppService permission,
        Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _menus = menus; _permission = permission; _snowflake = snowflake;
    }

    /// <summary>完整菜单树（管理端展示全部，含按钮）</summary>
    public async Task<List<MenuNodeDto>> TreeAsync()
    {
        var all = await _db.Queryable<SysMenu>().Where(m => m.IsDeleted == false).ToListAsync();
        return _permission.BuildTree(all);
    }

    /// <summary>新增/编辑菜单（按钮必须带权限码）</summary>
    public async Task SaveAsync(MenuSaveRequest req)
    {
        var type = req.type switch { "dir" => 0, "button" => 2, _ => 1 };
        if (type == 2 && string.IsNullOrEmpty(req.permission))
            throw new DomainException("按钮类型必须填写权限码（permission）");

        if (string.IsNullOrEmpty(req.id))
        {
            await _menus.AddAsync(new SysMenu
            {
                Id = _snowflake.NextId(),
                ParentId = string.IsNullOrEmpty(req.parentId) ? 0 : long.Parse(req.parentId),
                MenuName = req.name,
                MenuType = type,
                Path = req.path,
                Component = req.component,
                PermsCode = req.permission,
                Icon = req.icon,
                OrderNum = req.sort,
                Visible = req.visible,
                Status = req.status
            });
        }
        else
        {
            var id = long.Parse(req.id);
            var parentId = string.IsNullOrEmpty(req.parentId) ? 0 : long.Parse(req.parentId);
            if (id == parentId)
                throw new DomainException("父级不能是自己");
            await _menus.UpdateAsync(new SysMenu
            {
                Id = id,
                ParentId = parentId,
                MenuName = req.name,
                MenuType = type,
                Path = req.path,
                Component = req.component,
                PermsCode = req.permission,
                Icon = req.icon,
                OrderNum = req.sort,
                Visible = req.visible,
                Status = req.status
            });
        }
    }

    /// <summary>删除菜单（软删并级联全部子节点）</summary>
    public async Task<int> DeleteAsync(long id) => await _menus.SoftDeleteCascadeAsync(id);
}
