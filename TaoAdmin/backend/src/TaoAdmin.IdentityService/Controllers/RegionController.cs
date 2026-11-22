using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 行政区划入口（老板 2026-11-22 令⑪地址库）：
/// 只读查询——省市区街道四级联动数据源、关键词搜索、详情；不提供逐条新增/编辑/删除接口。
/// 更新走后台任务：点更新立即返回，进度实时可查，同一时间仅允许一个任务。
/// 查询接口登录即可用（联动组件在各业务表单复用，不挂管理权限码）；更新/恢复挂 sys:region:manage。
/// </summary>
[Route("regions")]
public class RegionController : BaseApiController
{
    private readonly RegionAppService _svc;
    private readonly RegionUpdateService _update;

    public RegionController(RegionAppService svc, RegionUpdateService update)
    {
        _svc = svc;
        _update = update;
    }

    /// <summary>按父级代码查子级（parentCode 不传查省级；直辖市下直接是区县）</summary>
    [HttpGet("children")]
    public async Task<ApiResult<List<RegionDto>>> Children([FromQuery] string? parentCode = null)
        => ApiOk(await _svc.GetChildrenAsync(parentCode));

    /// <summary>按关键词搜索（名称或全路径），最多 50 条</summary>
    [HttpGet("search")]
    public async Task<ApiResult<List<RegionDto>>> Search([FromQuery] string? keyword = null)
        => ApiOk(await _svc.SearchAsync(keyword));

    /// <summary>按区划代码查详情（不存在时 Data 为 null）</summary>
    [HttpGet("{code}")]
    public async Task<ApiResult<RegionDto>> GetByCode(string code)
        => ApiOk(await _svc.GetByCodeAsync(code));

    /// <summary>数据版本与更新状态：当前版本、抓取时间、各级条数，以及最近一次更新任务的进度</summary>
    [HttpGet("status")]
    public async Task<ApiResult<RegionStatusDto>> Status()
    {
        var meta = await _svc.GetMetaAsync();
        return ApiOk(new RegionStatusDto
        {
            Version = meta?.Version ?? "",
            Source = meta?.Source ?? "",
            FetchedAt = meta?.FetchedAt,
            ProvinceCount = meta?.ProvinceCount ?? 0,
            CityCount = meta?.CityCount ?? 0,
            CountyCount = meta?.CountyCount ?? 0,
            TownCount = meta?.TownCount ?? 0,
            TotalCount = meta?.TotalCount ?? 0,
            Update = _update.GetProgress()
        });
    }

    /// <summary>启动在线更新（后台执行，立即返回；已有任务在跑时返回失败）</summary>
    [HttpPost("update")]
    [Permission("sys:region:manage")]
    public ApiResult<object> StartUpdate()
    {
        var (ok, msg) = _update.StartOnlineUpdate();
        return ok ? ApiOk<object>(new { started = true }, msg) : ApiFail<object>(msg);
    }

    /// <summary>从内置数据包恢复（在线更新失败时的兜底，同样后台执行）</summary>
    [HttpPost("update/builtin")]
    [Permission("sys:region:manage")]
    public ApiResult<object> RestoreBuiltin()
    {
        var (ok, msg) = _update.StartBuiltinRestore();
        return ok ? ApiOk<object>(new { started = true }, msg) : ApiFail<object>(msg);
    }

    /// <summary>查询更新进度（轮询入口；刷新页面进度仍在）</summary>
    [HttpGet("update/progress")]
    [Authorize]
    public ApiResult<RegionUpdateProgress> UpdateProgress()
        => ApiOk(_update.GetProgress());
}

/// <summary>数据版本与更新状态视图</summary>
public class RegionStatusDto
{
    /// <summary>当前数据版本（如：民政部国家地名信息库·数据截止2025年12月31日）</summary>
    public string Version { get; set; } = "";
    /// <summary>数据来源说明</summary>
    public string Source { get; set; } = "";
    /// <summary>抓取/导入时间</summary>
    public DateTime? FetchedAt { get; set; }
    public int ProvinceCount { get; set; }
    public int CityCount { get; set; }
    public int CountyCount { get; set; }
    public int TownCount { get; set; }
    public int TotalCount { get; set; }
    /// <summary>最近一次更新任务进度</summary>
    public RegionUpdateProgress Update { get; set; } = new();
}
