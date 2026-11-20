using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>系统菜单表（sys_menu）。菜单类型：0 目录 / 1 菜单 / 2 按钮；按钮必须带 PermsCode。</summary>
[SugarTable("sys_menu", TableDescription = "系统菜单")]
public class SysMenu : BaseEntity
{
    /// <summary>父菜单ID，顶级为 0</summary>
    [SugarColumn(ColumnDescription = "父菜单ID 顶级为0")]
    public long ParentId { get; set; }

    /// <summary>菜单名称</summary>
    [SugarColumn(Length = 50, ColumnDescription = "菜单名称")]
    public string MenuName { get; set; } = string.Empty;

    /// <summary>菜单类型：0 目录 / 1 菜单 / 2 按钮</summary>
    [SugarColumn(ColumnDescription = "类型 0目录 1菜单 2按钮")]
    public int MenuType { get; set; }

    /// <summary>路由地址（目录/菜单）</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "路由地址")]
    public string? Path { get; set; }

    /// <summary>前端组件路径（菜单）</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "前端组件路径")]
    public string? Component { get; set; }

    /// <summary>权限码（按钮必填，如 sys:user:add）</summary>
    [SugarColumn(Length = 100, IsNullable = true, ColumnDescription = "权限码")]
    public string? PermsCode { get; set; }

    /// <summary>图标</summary>
    [SugarColumn(Length = 100, IsNullable = true, ColumnDescription = "图标")]
    public string? Icon { get; set; }

    /// <summary>排序号（越小越靠前）</summary>
    [SugarColumn(ColumnDescription = "排序号")]
    public int OrderNum { get; set; }

    /// <summary>是否显示（目录/菜单的显隐控制）</summary>
    [SugarColumn(ColumnDescription = "是否显示")]
    public bool Visible { get; set; } = true;

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;
}
