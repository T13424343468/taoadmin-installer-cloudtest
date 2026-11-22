using SqlSugar;

namespace TaoFiles.Api.Entities;

/// <summary>
/// 文件记录：每次上传登记一条；相同内容（MD5 一致）重复上传时直接复用已有记录。
/// 业务系统只需保存 <see cref="FileUrl"/>（相对路径），拼接服务地址即可访问。
/// </summary>
[SugarTable("sys_file", TableDescription = "文件记录")]
[SugarIndex("idx_file_created_id", nameof(CreatedAt), OrderByType.Asc, nameof(Id), OrderByType.Desc)]
[SugarIndex("idx_file_name", nameof(FileName), OrderByType.Asc)]
public class SysFile
{
    /// <summary>主键（雪花ID）</summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false, ColumnDescription = "雪花ID主键")]
    public long Id { get; set; }

    /// <summary>原文件名（仅展示，不用于磁盘存储）</summary>
    [SugarColumn(Length = 200, ColumnDescription = "原文件名")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>相对路径（业务系统保存这个字段，如 /uploads/2026/10/xxx.png）</summary>
    [SugarColumn(Length = 300, ColumnDescription = "相对路径")]
    public string FileUrl { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    [SugarColumn(ColumnDescription = "文件大小字节")]
    public long Size { get; set; }

    /// <summary>内容类型（MIME）</summary>
    [SugarColumn(Length = 100, IsNullable = true, ColumnDescription = "内容类型")]
    public string? ContentType { get; set; }

    /// <summary>MD5 指纹（去重/秒传依据）</summary>
    [SugarColumn(Length = 40, ColumnDescription = "MD5指纹")]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>上传人（对接业务系统时可传，可选）</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "上传人")]
    public string? UploadBy { get; set; }

    /// <summary>来源业务系统标识（可选，便于按系统筛选）</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "来源系统")]
    public string? Source { get; set; }

    /// <summary>创建时间</summary>
    [SugarColumn(ColumnDescription = "创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>软删除标记（true = 在回收站中）</summary>
    [SugarColumn(ColumnDescription = "软删除标记")]
    public bool IsDeleted { get; set; }

    /// <summary>移入回收站时间（未删除为 null；回收站排序与展示依据）</summary>
    [SugarColumn(IsNullable = true, ColumnDescription = "移入回收站时间")]
    public DateTime? DeletedAt { get; set; }
}
