using System.Text.Json;
using SqlSugar;
using TaoAdmin.Common.Entities;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 代码生成器：表设计（sys_codegen_table）。
/// 保存可视化表设计器画出的表结构（表名/中文名/字段清单及各字段的类型、控件、查询、校验配置），
/// 供「同步建库」与「一键生成全套增删改查前后端代码」使用。
/// </summary>
[SugarTable("sys_codegen_table", TableDescription = "代码生成器表设计")]
public class SysCodeGenTable : BaseEntity
{
    /// <summary>表名（英文，下划线风格，如 biz_contract）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "表名")]
    public string TableName { get; set; } = string.Empty;

    /// <summary>中文名（业务名，如「合同管理」）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "中文名")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>字段设计清单（JSON：CodegenFieldDesign 数组）</summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true, ColumnDescription = "字段设计JSON")]
    public string? FieldsJson { get; set; }

    /// <summary>备注</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "备注")]
    public string? Remark { get; set; }

    /// <summary>最近一次同步到数据库的时间（null 表示尚未同步）</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "最近同步时间")]
    public DateTime? LastSyncAt { get; set; }

    /// <summary>解析字段设计（损坏或为空时返回空清单）</summary>
    public List<CodegenFieldDesign> ParseFields()
    {
        if (string.IsNullOrWhiteSpace(FieldsJson)) return new List<CodegenFieldDesign>();
        try
        {
            return JsonSerializer.Deserialize<List<CodegenFieldDesign>>(FieldsJson) ?? new List<CodegenFieldDesign>();
        }
        catch (JsonException)
        {
            return new List<CodegenFieldDesign>();
        }
    }
}

/// <summary>单个字段的设计：物理属性 + 界面行为</summary>
public class CodegenFieldDesign
{
    /// <summary>字段名（英文）</summary>
    public string name { get; set; } = "";
    /// <summary>显示名（中文）</summary>
    public string label { get; set; } = "";
    /// <summary>数据类型：string/int/long/decimal/datetime/bool/text</summary>
    public string dataType { get; set; } = "string";
    /// <summary>长度（string/decimal 用）</summary>
    public int? length { get; set; }
    /// <summary>小数位（decimal 用）</summary>
    public int? precision { get; set; }
    /// <summary>是否可空</summary>
    public bool nullable { get; set; } = true;
    /// <summary>默认值（字符串表示）</summary>
    public string? defaultValue { get; set; }
    /// <summary>是否主键（生成器默认自带雪花 Id，此标记用于业务唯一键等场景预留）</summary>
    public bool isPrimary { get; set; }
    /// <summary>是否唯一索引</summary>
    public bool isUnique { get; set; }
    /// <summary>是否建普通索引</summary>
    public bool isIndex { get; set; }
    /// <summary>列表是否显示</summary>
    public bool showInList { get; set; } = true;
    /// <summary>表单控件：text/number/date/textarea/dict/switch</summary>
    public string control { get; set; } = "text";
    /// <summary>关联字典编码（control=dict 时必填）</summary>
    public string? dictCode { get; set; }
    /// <summary>查询方式：none/eq/like/range</summary>
    public string queryType { get; set; } = "none";
    /// <summary>校验规则：none/required/required+max（逗号分隔）</summary>
    public string? rules { get; set; }
}
