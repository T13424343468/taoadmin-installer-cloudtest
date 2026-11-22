using SqlSugar;
using TaoAdmin.Common.Entities;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 行政区划数据版本表（sys_region_meta）：记录当前生效数据的版本、来源、抓取时间与各级条数。
/// 全表仅一行（Id=1），随整包导入/更新一并写入（老板 2026-11-22 令⑪地址库：页面显示当前数据版本、抓取时间、条数）。
/// </summary>
[SugarTable("sys_region_meta", TableDescription = "行政区划数据版本")]
public class SysRegionMeta : BaseEntity
{
    /// <summary>固定主键 1（单行）</summary>
    public const long SingleRowId = 1;

    /// <summary>数据版本（如：民政部国家地名信息库·数据截止2025年12月31日）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "数据版本")]
    public string Version { get; set; } = string.Empty;

    /// <summary>数据来源说明（如：民政部国家地名信息库在线抓取 / 内置数据包）</summary>
    [SugarColumn(Length = 200, ColumnDescription = "数据来源")]
    public string Source { get; set; } = string.Empty;

    /// <summary>数据抓取/导入时间</summary>
    [SugarColumn(ColumnDescription = "抓取时间")]
    public DateTime FetchedAt { get; set; }

    /// <summary>省级条数</summary>
    [SugarColumn(ColumnDescription = "省级条数")]
    public int ProvinceCount { get; set; }

    /// <summary>地级市条数</summary>
    [SugarColumn(ColumnDescription = "地级市条数")]
    public int CityCount { get; set; }

    /// <summary>区县条数</summary>
    [SugarColumn(ColumnDescription = "区县条数")]
    public int CountyCount { get; set; }

    /// <summary>街道（乡镇）条数</summary>
    [SugarColumn(ColumnDescription = "街道乡镇条数")]
    public int TownCount { get; set; }

    /// <summary>总条数（各级之和）</summary>
    [SugarColumn(IsIgnore = true)]
    public int TotalCount => ProvinceCount + CityCount + CountyCount + TownCount;
}
