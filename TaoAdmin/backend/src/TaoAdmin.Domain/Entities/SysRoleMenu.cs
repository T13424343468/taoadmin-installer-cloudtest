using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>角色-菜单（含按钮）关联表（sys_role_menu）</summary>
[SugarTable("sys_role_menu", TableDescription = "角色菜单关联")]
public class SysRoleMenu : BaseEntity
{
    /// <summary>角色ID</summary>
    [SugarColumn(ColumnDescription = "角色ID")]
    public long RoleId { get; set; }

    /// <summary>菜单ID（目录/菜单/按钮）</summary>
    [SugarColumn(ColumnDescription = "菜单ID")]
    public long MenuId { get; set; }
}
