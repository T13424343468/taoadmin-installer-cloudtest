using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 站点参数表（sys_site_config）：站点门面参数，单行配置（键值对结构，常用键有默认值）。
/// 保存后前台启动拉取即生效，未配置的键使用内置默认值。
/// </summary>
[SugarTable("sys_site_config", TableDescription = "站点参数")]
public class SysSiteConfig : BaseEntity
{
    /// <summary>参数键（唯一，如 siteName / siteLogo / siteFooter）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "参数键")]
    public string ConfigKey { get; set; } = string.Empty;

    /// <summary>参数值</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "参数值")]
    public string? ConfigValue { get; set; }

    /// <summary>备注</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }
}
