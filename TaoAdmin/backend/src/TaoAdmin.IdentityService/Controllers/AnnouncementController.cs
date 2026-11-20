using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 公告通知入口：管理员发布/上下线/删除/分页查询；登录用户可读最新已发布公告。
/// </summary>
[Route("announcements")]
public class AnnouncementController : BaseApiController
{
    private readonly AnnouncementAppService _svc;
    public AnnouncementController(AnnouncementAppService svc) => _svc = svc;

    /// <summary>分页查询公告（管理端：含已下线，keyword 按标题模糊）</summary>
    [HttpGet]
    [Permission("sys:notice:manage")]
    public async Task<ApiResult<PageResult<AnnouncementItemDto>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, keyword));

    /// <summary>发布新公告（创建即上线）</summary>
    [HttpPost]
    [Permission("sys:notice:manage")]
    public async Task<ApiResult<object>> Publish([FromBody] AnnouncementSaveRequest req)
    {
        await _svc.PublishAsync(req, User.GetUserId(), User.Identity?.Name);
        return ApiOk("发布成功");
    }

    /// <summary>上/下线切换</summary>
    [HttpPut("{id}/status")]
    [Permission("sys:notice:manage")]
    public async Task<ApiResult<object>> SetStatus(string id, [FromBody] Dictionary<string, bool> body)
    {
        body.TryGetValue("online", out var online);
        await _svc.SetStatusAsync(long.Parse(id), online);
        return ApiOk(online ? "公告已上线" : "公告已下线");
    }

    /// <summary>删除公告（软删）</summary>
    [HttpDelete("{id}")]
    [Permission("sys:notice:manage")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _svc.DeleteAsync(long.Parse(id));
        return ApiOk("删除成功");
    }

    /// <summary>登录用户可见的最新公告（仅已发布，顶栏通知入口拉取）</summary>
    [HttpGet("latest")]
    [Authorize]
    public async Task<ApiResult<List<AnnouncementItemDto>>> Latest()
        => ApiOk(await _svc.LatestVisibleAsync());
}
