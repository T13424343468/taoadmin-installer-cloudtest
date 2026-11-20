using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 站点参数入口：管理员设置页维护站点名称/Logo/页脚，公开读取接口供前台渲染。
/// 读取接口匿名开放（登录页尚未持有令牌也要渲染门面），保存需站点配置管理权限。
/// </summary>
[Route("site-config")]
public class SiteConfigController : BaseApiController
{
    private readonly SiteConfigAppService _svc;
    public SiteConfigController(SiteConfigAppService svc) => _svc = svc;

    /// <summary>读取站点参数（匿名可读：未配置的键返回默认值，登录页与主布局拉取渲染）</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ApiResult<SiteConfigDto>> Get() => ApiOk(await _svc.GetAsync());

    /// <summary>保存站点参数（整体覆盖，保存即生效）</summary>
    [HttpPost]
    [HttpPut]
    [Permission("sys:site:manage")]
    public async Task<ApiResult<object>> Save([FromBody] SiteConfigSaveRequest req)
    {
        await _svc.SaveAsync(req);
        return ApiOk("保存成功");
    }
}
