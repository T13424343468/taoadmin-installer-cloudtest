using Microsoft.Extensions.Caching.Memory;
using SqlSugar;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 公告通知应用服务（SQLite 内存库真库测试）：
/// 发布即上线、上下线切换可见性、分页过滤、删除、最新可见公告口径。
/// </summary>
public class AnnouncementAppServiceTests
{
    private static (AnnouncementAppService Svc, SqlSugarClient Db) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysAnnouncement>();
        var svc = new AnnouncementAppService(db, new SnowflakeIdGenerator(1, 1));
        return (svc, db);
    }

    [Fact]
    public async Task PublishAsync_ShouldCreateOnlineAnnouncement_WithPublisherAndTime()
    {
        var (svc, db) = Create();
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "系统上线", content = "底座升级完成" }, 1001, "admin");
        var row = Assert.Single(db.Queryable<SysAnnouncement>().ToList());
        Assert.Equal(AnnouncementAppService.StatusPublished, row.Status);
        Assert.Equal("admin", row.PublisherName);
        Assert.NotNull(row.PublishTime);
    }

    [Fact]
    public async Task PublishAsync_BlankTitle_ShouldThrow()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.PublishAsync(new AnnouncementSaveRequest { title = "  " }, 1, "admin"));
    }

    [Fact]
    public async Task LatestVisibleAsync_ShouldContainOnlyPublished()
    {
        var (svc, _) = Create();
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "公告一" }, 1, "admin");
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "公告二" }, 1, "admin");

        var latest = await svc.LatestVisibleAsync();
        Assert.Equal(2, latest.Count);

        // 下线公告二：最新可见只剩公告一
        var offlineId = latest[0].id; // 最新发布在前
        await svc.SetStatusAsync(long.Parse(offlineId), online: false);
        latest = await svc.LatestVisibleAsync();
        Assert.Single(latest);
        Assert.Equal("公告一", latest[0].title); // 公告一（较旧）仍在

        // 重新上线：恢复可见
        await svc.SetStatusAsync(long.Parse(offlineId), online: true);
        Assert.Equal(2, (await svc.LatestVisibleAsync()).Count);
    }

    [Fact]
    public async Task PageAsync_ShouldIncludeOfflineAndFilterByKeyword()
    {
        var (svc, _) = Create();
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "维护公告" }, 1, "admin");
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "节假日安排" }, 1, "admin");
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "发版说明" }, 1, "admin");

        var page = await svc.PageAsync(1, 10, "公告");
        Assert.Equal(1, page.Total); // 仅「维护公告」命中标题关键词

        // 管理端分页包含已下线
        var offlineId = page.Items.First(i => i.title == "维护公告").id;
        await svc.SetStatusAsync(long.Parse(offlineId), online: false);
        var allPage = await svc.PageAsync(1, 10, null);
        Assert.Equal(3, allPage.Total);
        Assert.Contains(allPage.Items, i => i.title == "维护公告" && i.status == AnnouncementAppService.StatusOffline);
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDelete_NotVisibleInLatest()
    {
        var (svc, db) = Create();
        await svc.PublishAsync(new AnnouncementSaveRequest { title = "待删除" }, 1, "admin");
        var latest = await svc.LatestVisibleAsync();
        await svc.DeleteAsync(long.Parse(latest[0].id));
        Assert.Empty(await svc.LatestVisibleAsync());
        var row = Assert.Single(db.Queryable<SysAnnouncement>().ToList());
        Assert.True(row.IsDeleted);
    }

    [Fact]
    public async Task SetStatusAsync_NotExist_ShouldThrow404()
    {
        var (svc, _) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() => svc.SetStatusAsync(999, true));
        Assert.Equal(404, ex.Code);
    }
}
