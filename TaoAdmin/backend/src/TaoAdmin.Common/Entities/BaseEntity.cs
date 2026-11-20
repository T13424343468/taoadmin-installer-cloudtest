using SqlSugar;

namespace TaoAdmin.Common.Entities;

/// <summary>
/// 所有实体的公共基类：
/// Id（雪花长整型主键）、CreatedAt / UpdatedAt 审计时间、IsDeleted 软删除标记。
/// </summary>
public abstract class BaseEntity
{
    /// <summary>主键：雪花ID（非自增）</summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false, ColumnDescription = "雪花ID主键")]
    public long Id { get; set; }

    /// <summary>创建时间</summary>
    [SugarColumn(ColumnDescription = "创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>更新时间</summary>
    [SugarColumn(ColumnDescription = "更新时间")]
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>软删除标记：true 已删除</summary>
    [SugarColumn(ColumnDescription = "软删除标记")]
    public bool IsDeleted { get; set; }
}
