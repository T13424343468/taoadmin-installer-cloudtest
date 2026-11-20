using System.Security.Cryptography;
using SqlSugar;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Storage;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>上传结果（返回给前端展示与业务落库用）</summary>
public class FileUploadResult
{
    /// <summary>文件记录 ID</summary>
    public long id { get; set; }
    /// <summary>原文件名（展示用）</summary>
    public string fileName { get; set; } = "";
    /// <summary>相对路径（业务系统保存它）</summary>
    public string fileUrl { get; set; } = "";
    /// <summary>大小（字节）</summary>
    public long size { get; set; }
    /// <summary>内容类型</summary>
    public string? contentType { get; set; }
    /// <summary>MD5 指纹</summary>
    public string md5 { get; set; } = "";
    /// <summary>true=命中秒传（同 MD5 已存在，未重复占用存储）</summary>
    public bool deduplicated { get; set; }
}

/// <summary>
/// 文件中心应用服务：上传（MD5 秒传去重）、分页查询、下载（取物理流）、删除（软删+物理删）。
/// 存储经 IFileStorageProvider 抽象：本地盘 / MinIO 对象存储可配置切换，业务代码零改动。
/// </summary>
public class FileAppService(ISqlSugarClient db, IFileStorageProvider storage, SnowflakeIdGenerator snowflake)
{
    /// <summary>上传大小上限（5MB，与前端导入上限一致）</summary>
    public const long MaxUploadSize = 5 * 1024 * 1024;

    /// <summary>允许的扩展名白名单（安全闸：拒绝可执行/脚本类文件）</summary>
    public static readonly string[] AllowedExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt", ".md", ".csv", ".xlsx", ".xls", ".docx", ".doc", ".zip", ".mp3", ".mp4"];

    /// <summary>分页查询文件记录（管理端，keyword 按原文件名模糊）</summary>
    public async Task<PageResult<SysFile>> PageAsync(int pageIndex, int pageSize, string? keyword)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize is < 1 or > 100) pageSize = 10;
        var query = db.Queryable<SysFile>().Where(f => f.IsDeleted == false);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(f => f.FileName.Contains(keyword.Trim()));
        var total = await query.CountAsync();
        var items = await query.OrderBy(f => f.Id, OrderByType.Desc)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PageResult<SysFile>(total, pageIndex, pageSize, items);
    }

    /// <summary>
    /// 上传文件：校验大小与扩展名白名单 → 算 MD5 → 命中同 MD5 未删记录直接秒传复用；
    /// 否则经存储 Provider 落盘并登记 sys_file。
    /// </summary>
    public async Task<FileUploadResult> UploadAsync(Stream content, string originalName, string? contentType, long userId, string? userName)
    {
        if (content is null || !content.CanRead) throw new DomainException("文件内容不可读", 400);
        var name = (originalName ?? "").Trim();
        if (name.Length == 0) throw new DomainException("文件名不能为空", 400);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) throw new DomainException($"不支持的文件类型：{ext}", 400);

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        var bytes = ms.ToArray();
        if (bytes.Length == 0) throw new DomainException("文件内容为空", 400);
        if (bytes.Length > MaxUploadSize) throw new DomainException("文件超过 5MB 上限", 400);

        var md5 = Convert.ToHexString(MD5.HashData(bytes));

        // 秒传：同 MD5 未删记录直接复用（不重复占用存储）
        var existed = await db.Queryable<SysFile>().FirstAsync(f => f.Md5 == md5 && f.IsDeleted == false);
        if (existed is not null)
        {
            return new FileUploadResult
            {
                id = existed.Id, fileName = name, fileUrl = existed.FileUrl,
                size = existed.Size, contentType = existed.ContentType, md5 = md5, deduplicated = true
            };
        }

        var relative = await storage.SaveAsync(new MemoryStream(bytes), name, contentType ?? "application/octet-stream");
        var row = new SysFile
        {
            Id = snowflake.NextId(),
            FileName = name,
            FileUrl = relative,
            Size = bytes.Length,
            ContentType = contentType,
            Md5 = md5,
            UploadBy = string.IsNullOrWhiteSpace(userName) ? userId.ToString() : userName.Trim(),
            UploadUserId = userId
        };
        await db.Insertable(row).ExecuteCommandAsync();
        return new FileUploadResult
        {
            id = row.Id, fileName = name, fileUrl = relative,
            size = bytes.Length, contentType = contentType, md5 = md5, deduplicated = false
        };
    }

    /// <summary>打开文件物理流（下载用）；未删记录才允许读</summary>
    public async Task<(SysFile Row, Stream Content)?> OpenAsync(string fileUrl)
    {
        var row = await db.Queryable<SysFile>().FirstAsync(f => f.FileUrl == fileUrl && f.IsDeleted == false);
        if (row is null) return null;
        var stream = await storage.OpenAsync(row.FileUrl);
        return stream is null ? null : (row, stream);
    }

    /// <summary>删除文件（软删 + 删物理文件；秒传复用场景下物理文件可能已不存在，容忍）</summary>
    public async Task DeleteAsync(long id)
    {
        var row = await db.Queryable<SysFile>().FirstAsync(f => f.Id == id && f.IsDeleted == false)
            ?? throw new DomainException("文件不存在", 404);
        row.IsDeleted = true;
        row.UpdatedAt = DateTime.Now;
        await db.Updateable(row).ExecuteCommandAsync();
        await storage.DeleteAsync(row.FileUrl);
    }
}
