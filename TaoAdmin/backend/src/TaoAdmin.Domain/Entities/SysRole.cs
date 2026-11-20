using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>系统角色表（sys_role）</summary>
[SugarTable("sys_role", TableDescription = "系统角色")]
public class SysRole : BaseEntity
{
    /// <summary>角色名称</summary>
    [SugarColumn(Length = 50, ColumnDescription = "角色名称")]
    public string RoleName { get; set; } = string.Empty;

    /// <summary>角色编码（唯一），如 admin / common</summary>
    [SugarColumn(Length = 50, ColumnDescription = "角色编码")]
    public string RoleCode { get; set; } = string.Empty;

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;

    /// <summary>备注</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
