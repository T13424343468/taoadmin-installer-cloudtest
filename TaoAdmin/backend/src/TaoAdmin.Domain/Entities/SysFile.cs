using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 文件记录表（sys_file）：每次上传登记一条；相同 MD5 重复上传时直接复用已有记录（去重）。
/// 业务系统保存 FileUrl（相对路径），拼接网关域名即可访问。
/// </summary>
[SugarTable("sys_file", TableDescription = "文件记录")]
[SugarIndex("idx_file_created_id", nameof(BaseEntity.CreatedAt), OrderByType.Asc, nameof(BaseEntity.Id), OrderByType.Desc)]
[SugarIndex("idx_file_name", nameof(FileName), OrderByType.Asc)]
public class SysFile : BaseEntity, TaoAdmin.Common.Entities.ITenantEntity
{
    /// <summary>原文件名（仅作展示，不用于磁盘存储）</summary>
    [SugarColumn(Length = 200, ColumnDescription = "原文件名")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>相对路径（如 /uploads/2026/10/xxx.png，业务系统存这个）</summary>
    [SugarColumn(Length = 300, ColumnDescription = "相对路径")]
    public string FileUrl { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    [SugarColumn(ColumnDescription = "文件大小字节")]
    public long Size { get; set; }

    /// <summary>内容类型（MIME）</summary>
    [SugarColumn(Length = 100, IsNullable = true, ColumnDescription = "内容类型")]
    public string? ContentType { get; set; }

    /// <summary>MD5 指纹（秒传/去重依据）</summary>
    [SugarColumn(Length = 40, ColumnDescription = "MD5指纹")]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>上传人姓名</summary>
    [SugarColumn(Length = 50, IsNullable = true, ColumnDescription = "上传人")]
    public string? UploadBy { get; set; }

    /// <summary>上传人 ID</summary>
    [SugarColumn(ColumnDescription = "上传人ID")]
    public long UploadUserId { get; set; }

    /// <summary>所属租户（0 = 平台）</summary>
    [SugarColumn(ColumnDescription = "所属租户ID")]
    public long TenantId { get; set; }
}
