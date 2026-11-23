using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 密码重置令牌表（sys_password_reset_token，⑱自助找回密码）：
/// 只存 SHA-256 哈希（明文令牌仅出现在邮件里），一次性使用、限时失效。
/// </summary>
[SugarTable("sys_password_reset_token", TableDescription = "密码重置令牌")]
public class SysPasswordResetToken : BaseEntity
{
    /// <summary>申请重置的账号</summary>
    [SugarColumn(Length = 50, ColumnDescription = "申请账号")]
    public string UserName { get; set; } = string.Empty;

    /// <summary>令牌 SHA-256 哈希（hex）</summary>
    [SugarColumn(Length = 64, ColumnDescription = "令牌哈希")]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>过期时间</summary>
    [SugarColumn(ColumnDescription = "过期时间")]
    public DateTime ExpireAt { get; set; }

    /// <summary>使用时间（null=未使用；一次性令牌用后即废）</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "使用时间")]
    public DateTime? UsedAt { get; set; }
}
