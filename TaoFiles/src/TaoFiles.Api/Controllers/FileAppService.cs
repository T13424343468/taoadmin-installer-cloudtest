using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using TaoFiles.Api.Entities;
using TaoFiles.Api.Storage;
using SqlSugar;

namespace TaoFiles.Api.Controllers;

// ============ DTO ============
public class UploadResult
{
    /// <summary>文件 ID</summary>
    public string id { get; set; } = "";
    /// <summary>原文件名</summary>
    public string fileName { get; set; } = "";
    /// <summary>相对路径（业务系统保存这个）</summary>
    public string fileUrl { get; set; } = "";
    /// <summary>大小（字节）</summary>
    public long size { get; set; }
    /// <summary>MD5</summary>
    public string md5 { get; set; } = "";
    /// <summary>是否命中复用（true = 秒传，未产生新文件）</summary>
    public bool reused { get; set; }
}

public class FileDto
{
    public string id { get; set; } = "";
    public string fileName { get; set; } = "";
    public string fileUrl { get; set; } = "";
    public long size { get; set; }
    public string? contentType { get; set; }
    public string md5 { get; set; } = "";
    public string? uploadBy { get; set; }
    public string? source { get; set; }
    public DateTime createdAt { get; set; }
    /// <summary>移入回收站时间（仅回收站列表有值）</summary>
    public DateTime? deletedAt { get; set; }
}

public class PageOf<T>
{
    public int total { get; set; }
    public List<T> list { get; set; } = new();
    /// <summary>下一页游标（亿级深翻页用：下一页请求带上它可恒定速度定位；为空表示没有下一页）</summary>
    public string? nextCursor { get; set; }
}

/// <summary>
/// 文件应用服务：上传（MD5 去重/秒传）、分页列表、删除。
/// 同一个文件反复上传只保留一份物理文件与一条记录——这正是「可反复使用」的核心。
/// 亿级优化：总数按筛选条件缓存 5 分钟；深翻页走 (CreatedAt, Id) 游标（Keyset），恒定速度不随页码衰减。
/// 上传体积上限（⑬）走 IUploadLimitProvider：站点配置为唯一事实源，超限返回明确中文报错。
/// </summary>
public class FileAppService(ISqlSugarClient db, IFileStorageProvider storage, IUploadLimitProvider limitProvider, IMemoryCache cache, IConfiguration? config = null)
{
    private string[] AllowedExt => ((config?["FileStorage:AllowedExtensions"])
        ?? ".jpg,.jpeg,.png,.gif,.webp,.svg,.pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.zip,.rar,.7z,.mp3,.mp4,.txt,.md,.csv")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>上传：校验扩展名白名单与大小限制 → MD5 去重 → 落盘 → 登记记录</summary>
    public async Task<UploadResult> UploadAsync(IFormFile file, string? uploadBy, string? source)
    {
        if (file is null || file.Length == 0) throw new DomainException("请选择要上传的文件", 400);

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext)) throw new DomainException($"不支持的文件类型：{ext}", 400);
        var limitBytes = await limitProvider.GetLimitBytesAsync();
        if (file.Length > limitBytes)
            throw new DomainException(
                $"文件体积 {file.Length / 1048576.0:0.#}MB 超过上传上限 {limitBytes / 1048576.0:0.#}MB，请压缩后上传或联系管理员调整站点配置中的「上传体积上限」", 400);

        await using var stream = file.OpenReadStream();

        // 计算 MD5 用于去重
        string md5;
        using (var md5Hash = MD5.Create())
            md5 = Convert.ToHexString(md5Hash.ComputeHash(stream)).ToLowerInvariant();
        stream.Position = 0;

        var existing = await db.Queryable<SysFile>()
            .FirstAsync(f => f.Md5 == md5 && f.IsDeleted == false);
        if (existing != null)
        {
            // 命中复用：物理文件与记录都不新建（秒传）
            return new UploadResult
            {
                id = existing.Id.ToString(), fileName = existing.FileName, fileUrl = existing.FileUrl,
                size = existing.Size, md5 = existing.Md5, reused = true
            };
        }

        var relativePath = await storage.SaveAsync(stream, file.FileName);
        var entity = new SysFile
        {
            Id = Snowflake.NextId(),
            FileName = file.FileName,
            FileUrl = relativePath,
            Size = file.Length,
            ContentType = file.ContentType,
            Md5 = md5,
            UploadBy = uploadBy,
            Source = source
        };
        await db.Insertable(entity).ExecuteCommandAsync();

        return new UploadResult
        {
            id = entity.Id.ToString(), fileName = entity.FileName, fileUrl = entity.FileUrl,
            size = entity.Size, md5 = entity.Md5, reused = false
        };
    }

    /// <summary>分页列表（按文件名/来源筛选；cursor=上一页 nextCursor 时走游标深翻页）</summary>
    public async Task<PageOf<FileDto>> PageAsync(int pageIndex, int pageSize, string? keyword, string? source, string? cursor = null)
    {
        var query = db.Queryable<SysFile>()
            .Where(f => f.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), f => f.FileName.Contains(keyword!))
            .WhereIF(!string.IsNullOrWhiteSpace(source), f => f.Source == source);

        // 总数按筛选条件缓存 5 分钟：亿级表上 COUNT 全表代价高，翻页期间复用同一口径
        var cacheKey = $"fc:total:{keyword?.Trim()}|{source?.Trim()}";
        var total = await cache.GetOrCreateAsync(cacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await query.CountAsync();
        });

        // 游标翻页：按 (CreatedAt, Id) 复合键定位，索引直达起点后顺序取一页
        var cur = ParseCursor(cursor);
        if (cur.HasValue)
        {
            query = query.Where(f => f.CreatedAt < cur!.Value.time
                                     || (f.CreatedAt == cur.Value.time && f.Id < cur.Value.id));
        }

        var list = await query
            .OrderBy(f => f.CreatedAt, OrderByType.Desc)
            .OrderBy(f => f.Id, OrderByType.Desc)
            .Take(pageSize)
            .ToListAsync();
        string? nextCursor = list.Count == pageSize && list.Count > 0
            ? EncodeCursor(list[^1].CreatedAt, list[^1].Id)
            : null;
        return new PageOf<FileDto>
        {
            total = total,
            nextCursor = nextCursor,
            list = list.Select(ToDto).ToList()
        };
    }

    /// <summary>游标格式：Base64("CreatedAt.Ticks|Id")；解析失败按无游标处理（回退 OFFSET 翻页）</summary>
    internal static string EncodeCursor(DateTime createdAt, long id)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{createdAt.Ticks}|{id}"));

    internal static (DateTime time, long id)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !long.TryParse(parts[1], out var id))
                return null;
            return (new DateTime(ticks), id);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// 删除（移入回收站）：记录保留（IsDeleted + DeletedAt），磁盘文件不动。
    /// 依据老板 2026-11-18 拍板阶段二安全闸：删除可还原必留退路——彻底删除须经回收站二次确认后发起。
    /// </summary>
    public async Task DeleteAsync(string id)
    {
        var entity = await FindAliveAsync(id);
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.Now;
        await db.Updateable(entity).ExecuteCommandAsync();
    }

    /// <summary>回收站分页（仅已删记录，按删除时间倒序；回收站量小，总数直接计数不缓存）</summary>
    public async Task<PageOf<FileDto>> RecycleBinPageAsync(int pageIndex, int pageSize, string? keyword = null)
    {
        var query = db.Queryable<SysFile>()
            .Where(f => f.IsDeleted == true)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), f => f.FileName.Contains(keyword!));

        var total = await query.CountAsync();
        var list = await query
            .OrderBy(f => f.DeletedAt, OrderByType.Desc)
            .OrderBy(f => f.Id, OrderByType.Desc)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        return new PageOf<FileDto>
        {
            total = total,
            list = list.Select(ToDto).ToList()
        };
    }

    /// <summary>从回收站恢复（还原记录，磁盘文件从未删除，恢复即重新可用）</summary>
    public async Task RestoreAsync(string id)
    {
        var entity = await FindRecycledAsync(id);
        entity.IsDeleted = false;
        entity.DeletedAt = null;
        await db.Updateable(entity).ExecuteCommandAsync();
    }

    /// <summary>彻底删除（物理删记录 + 清理磁盘文件，不可恢复；调用方须已做二次确认）</summary>
    public async Task PurgeAsync(string id)
    {
        var entity = await FindRecycledAsync(id);
        await db.Deleteable(entity).ExecuteCommandAsync();
        await storage.DeleteAsync(entity.FileUrl);
    }

    private async Task<SysFile> FindAliveAsync(string id)
    {
        var fid = long.Parse(id);
        return await db.Queryable<SysFile>().FirstAsync(f => f.Id == fid && f.IsDeleted == false)
            ?? throw new DomainException("文件不存在", 404);
    }

    private async Task<SysFile> FindRecycledAsync(string id)
    {
        var fid = long.Parse(id);
        return await db.Queryable<SysFile>().FirstAsync(f => f.Id == fid && f.IsDeleted == true)
            ?? throw new DomainException("文件不在回收站中", 404);
    }

    private FileDto ToDto(SysFile f) => new()
    {
        id = f.Id.ToString(), fileName = f.FileName, fileUrl = f.FileUrl, size = f.Size,
        contentType = f.ContentType, md5 = f.Md5, uploadBy = f.UploadBy,
        source = f.Source, createdAt = f.CreatedAt, deletedAt = f.DeletedAt
    };
}

/// <summary>业务异常（统一返回给调用方）</summary>
public class DomainException(string message, int code = 400) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>简易雪花 ID 生成器（单实例足够；多实例可用起始时间戳+机器号扩展）</summary>
public static class Snowflake
{
    private static long _lastTs = -1L, _seq;
    private static readonly object Lock = new();
    private const long Epoch = 638000000000000000L; // 自定义纪元，保证足够长期唯一

    public static long NextId()
    {
        lock (Lock)
        {
            var ts = DateTime.UtcNow.Ticks - Epoch;
            if (ts == _lastTs)
            {
                _seq = (_seq + 1) & 0xFFF;
                if (_seq == 0) { while (ts <= _lastTs) ts = DateTime.UtcNow.Ticks - Epoch; }
            }
            else _seq = 0;
            _lastTs = ts;
            return (ts << 12) | _seq;
        }
    }
}
