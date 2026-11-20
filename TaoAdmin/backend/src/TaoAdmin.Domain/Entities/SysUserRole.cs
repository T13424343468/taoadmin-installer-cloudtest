using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>用户-角色关联表（sys_user_role）</summary>
[SugarTable("sys_user_role", TableDescription = "用户角色关联")]
public class SysUserRole : BaseEntity
{
    /// <summary>用户ID</summary>
    [SugarColumn(ColumnDescription = "用户ID")]
    public long UserId { get; set; }

    /// <summary>角色ID</summary>
    [SugarColumn(ColumnDescription = "角色ID")]
    public long RoleId { get; set; }
}
