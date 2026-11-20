using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>系统用户表（sys_user）</summary>
[SugarTable("sys_user", TableDescription = "系统用户")]
public class SysUser : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>所属租户 ID（0 = 平台层）</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }

    /// <summary>登录账号（唯一）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "登录账号")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>密码（BCrypt 摘要）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "密码（BCrypt）")]
    public string Password { get; set; } = string.Empty;

    /// <summary>昵称</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "昵称")]
    public string? NickName { get; set; }

    /// <summary>手机号</summary>
    [SugarColumn(Length = 20, IsNullable = true, ColumnDescription = "手机号")]
    public string? Phone { get; set; }

    /// <summary>邮箱</summary>
    [SugarColumn(Length = 100, IsNullable = true, ColumnDescription = "邮箱")]
    public string? Email { get; set; }

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;

    /// <summary>最后登录时间</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最后登录时间")]
    public DateTime? LastLoginTime { get; set; }

    /// <summary>头像（文件中心相对路径或完整 URL，空为未设置；个人中心自助维护）</summary>
    [SugarColumn(Length = 255, IsNullable = true, ColumnDescription = "头像")]
    public string? Avatar { get; set; }

    /// <summary>最后改密时间（密码策略强制改密基准；NULL=策略生效前的存量口令，判定时回退建号时间）</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最后改密时间")]
    public DateTime? PasswordUpdatedAt { get; set; }
}
