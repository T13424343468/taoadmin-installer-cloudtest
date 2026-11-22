using SqlSugar;
using TaoAdmin.Common.Entities;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 行政区划表（sys_region）：全国省/市/县/街道（乡镇）四级行政区划。
/// 数据来源民政部国家地名信息库（每年 1 月发布截至上年末的全国四级区划代码）。
/// 只读基准数据：系统仅提供查看与搜索，除整包更新外不做逐条编辑（老板 2026-11-22 令⑪地址库）。
/// </summary>
[SugarTable("sys_region", TableDescription = "行政区划")]
[SugarIndex("idx_region_code", nameof(Code), OrderByType.Asc)]
[SugarIndex("idx_region_parent_code", nameof(ParentCode), OrderByType.Asc)]
public class SysRegion : BaseEntity
{
    /// <summary>区划代码（国标码：省 6 位 XXXX0000、地级 6 位 XXXY00、县级 6 位、乡级 9 位，如 110101、110101001）</summary>
    [SugarColumn(Length = 12, ColumnDescription = "区划代码")]
    public string Code { get; set; } = string.Empty;

    /// <summary>区划名称（如：东城区、东华门街道）</summary>
    [SugarColumn(Length = 50, ColumnDescription = "区划名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>父级区划代码（省级为空字符串；直辖市下辖区县，层级可跳级）</summary>
    [SugarColumn(Length = 12, ColumnDescription = "父级区划代码")]
    public string ParentCode { get; set; } = string.Empty;

    /// <summary>层级：1 省，2 地级市，3 区县，4 街道（乡镇）</summary>
    [SugarColumn(ColumnDescription = "层级 1省 2市 3县 4街道")]
    public int Level { get; set; }

    /// <summary>全路径（如：北京市 / 东城区 / 东华门街道），导入时计算，搜索结果展示用</summary>
    [SugarColumn(Length = 200, ColumnDescription = "全路径")]
    public string FullPath { get; set; } = string.Empty;
}
