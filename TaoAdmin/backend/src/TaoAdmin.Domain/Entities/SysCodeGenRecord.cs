using SqlSugar;
using TaoAdmin.Common.Entities;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 代码生成器：生成记录（sys_codegen_record）。
/// 每次一键生成落一条记录，保存生成出的全套文件（后端实体/服务/接口 + 前端接口定义/列表页/表单弹窗），
/// 供查看、下载与重复生成时复用元数据。
/// </summary>
[SugarTable("sys_codegen_record", TableDescription = "代码生成器生成记录")]
public class SysCodeGenRecord : BaseEntity
{
    /// <summary>关联的表设计 Id</summary>
    [SugarColumn(ColumnDescription = "表设计Id")]
    public long TableId { get; set; }

    /// <summary>表名（冗余，便于查看）</summary>
    [SugarColumn(Length = 100, ColumnDescription = "表名")]
    public string TableName { get; set; } = string.Empty;

    /// <summary>生成方式：generate 全量生成 / regenerate 重复生成（覆盖生成区）</summary>
    [SugarColumn(Length = 20, ColumnDescription = "生成方式")]
    public string Mode { get; set; } = "generate";

    /// <summary>生成出的文件清单（JSON：[{path, content}]，内容带统一标记头）</summary>
    [SugarColumn(ColumnDataType = "TEXT", IsNullable = true, ColumnDescription = "生成文件JSON")]
    public string? FilesJson { get; set; }

    /// <summary>文件数</summary>
    [SugarColumn(ColumnDescription = "文件数")]
    public int FileCount { get; set; }

    /// <summary>耗时（毫秒）</summary>
    [SugarColumn(ColumnDescription = "耗时毫秒")]
    public long ElapsedMs { get; set; }
}
