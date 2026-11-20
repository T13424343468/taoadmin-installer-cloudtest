using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>公告保存请求（新增发布；不提供整体编辑正文以保持发布记录可追溯，需要修改可下线后重新发布）</summary>
public class AnnouncementSaveRequest
{
    /// <summary>公告标题</summary>
    public string title { get; set; } = "";
    /// <summary>公告内容</summary>
    public string? content { get; set; }
}

/// <summary>
/// 公告通知应用服务：发布、上下线、分页查询（管理端）与最新可见公告（登录用户）。
/// </summary>
public class AnnouncementAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
{
    /// <summary>公告状态：已下线</summary>
    public const int StatusOffline = 0;
    /// <summary>公告状态：已发布（上线）</summary>
    public const int StatusPublished = 1;

    /// <summary>登录用户最新公告条数上限</summary>
    public const int LatestLimit = 10;

    /// <summary>发布新公告（创建即上线，发布时间落库）</summary>
    public async Task PublishAsync(AnnouncementSaveRequest req, long publisherId, string? publisherName)
    {
        Validate(req);
        await db.Insertable(new SysAnnouncement
        {
            Id = snowflake.NextId(),
            Title = req.title.Trim(),
            Content = req.content?.Trim(),
            Status = StatusPublished,
            PublisherName = string.IsNullOrWhiteSpace(publisherName) ? publisherId.ToString() : publisherName.Trim(),
            PublishTime = DateTime.Now
        }).ExecuteCommandAsync();
    }

    /// <summary>上/下线切换（Status 置位；下线后登录用户不可见，管理端仍可查）</summary>
    public async Task SetStatusAsync(long id, bool online)
    {
        var row = await db.Queryable<SysAnnouncement>().FirstAsync(a => a.Id == id && a.IsDeleted == false)
            ?? throw new DomainException("公告不存在", 404);
        row.Status = online ? StatusPublished : StatusOffline;
        row.UpdatedAt = DateTime.Now;
        if (online) row.PublishTime = row.PublishTime ?? DateTime.Now;
        await db.Updateable(row).ExecuteCommandAsync();
    }

    /// <summary>删除公告（软删）</summary>
    public async Task DeleteAsync(long id)
    {
        var row = await db.Queryable<SysAnnouncement>().FirstAsync(a => a.Id == id && a.IsDeleted == false)
            ?? throw new DomainException("公告不存在", 404);
        row.IsDeleted = true;
        row.UpdatedAt = DateTime.Now;
        await db.Updateable(row).ExecuteCommandAsync();
    }

    /// <summary>分页查询（管理端：含已下线；keyword 按标题模糊，总数与明细同口径）</summary>
    public async Task<PageResult<AnnouncementItemDto>> PageAsync(int pageIndex, int pageSize, string? keyword)
    {
        var query = db.Queryable<SysAnnouncement>()
            .Where(a => a.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), a => a.Title.Contains(keyword!.Trim()));
        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(a => a.PublishTime)
            .OrderByDescending(a => a.Id)
            .Skip(Math.Max(0, (pageIndex - 1) * pageSize)).Take(pageSize)
            .ToListAsync();
        var items = rows.Select(Map).ToList();
        return new PageResult<AnnouncementItemDto>(total, pageIndex, pageSize, items);
    }

    /// <summary>登录用户可见的最新公告（仅已发布，按发布时间倒序取 LatestLimit 条）</summary>
    public async Task<List<AnnouncementItemDto>> LatestVisibleAsync()
    {
        var rows = await db.Queryable<SysAnnouncement>()
            .Where(a => a.IsDeleted == false && a.Status == StatusPublished)
            .OrderByDescending(a => a.PublishTime)
            .OrderByDescending(a => a.Id)
            .Take(LatestLimit)
            .ToListAsync();
        return rows.Select(Map).ToList();
    }

    private static AnnouncementItemDto Map(SysAnnouncement a) => new()
    {
        id = a.Id.ToString(),
        title = a.Title,
        content = a.Content,
        status = a.Status,
        publisherName = a.PublisherName,
        publishTime = a.PublishTime,
        createdAt = a.CreatedAt
    };

    private static void Validate(AnnouncementSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.title))
            throw new DomainException("公告标题不能为空", 400);
        if (req.title.Trim().Length > 100)
            throw new DomainException("公告标题不超过 100 个字符", 400);
        if (!string.IsNullOrEmpty(req.content) && req.content.Length > 20000)
            throw new DomainException("公告内容过长", 400);
    }
}
