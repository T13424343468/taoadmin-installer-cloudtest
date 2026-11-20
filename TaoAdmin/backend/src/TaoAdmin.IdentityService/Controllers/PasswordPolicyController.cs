using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 密码策略入口：管理员在系统管理页维护最小长度/字符类数/强制改密天数，
/// 读取接口登录即可（改密弹窗与建人预检都要按当前策略提示），保存需密码策略管理权限。
/// </summary>
[Route("pwd-policy")]
public class PasswordPolicyController : BaseApiController
{
    private readonly PasswordPolicyAppService _svc;
    public PasswordPolicyController(PasswordPolicyAppService svc) => _svc = svc;

    /// <summary>读取当前密码策略（未配置的键返回内置默认值）</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ApiResult<PasswordPolicy>> Get() => ApiOk(await _svc.GetAsync());

    /// <summary>保存密码策略（整体覆盖，保存即清缓存立即生效）</summary>
    [HttpPost]
    [HttpPut]
    [Permission("sys:pwdpolicy:manage")]
    public async Task<ApiResult<object>> Save([FromBody] PasswordPolicy req)
    {
        await _svc.SaveAsync(req);
        return ApiOk("保存成功");
    }
}
