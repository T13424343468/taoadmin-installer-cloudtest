using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>个人中心入口：「自己管自己」——资料与密码。业务在 ProfileAppService。</summary>
[Route("profile")]
public class ProfileController : BaseApiController
{
    private readonly ProfileAppService _svc;

    public ProfileController(ProfileAppService svc) => _svc = svc;

    /// <summary>我的资料</summary>
    [HttpGet]
    public async Task<ApiResult<ProfileDto>> Get() => ApiOk(await _svc.GetAsync(User.GetUserId()));

    /// <summary>改自己的资料（昵称/头像/手机/邮箱，保存即生效）</summary>
    [HttpPut]
    public async Task<ApiResult<object>> Save([FromBody] ProfileSaveRequest req)
    {
        await _svc.UpdateBaseAsync(User.GetUserId(), req);
        return ApiOk("资料已更新");
    }

    /// <summary>改自己的密码（旧密码必校验；改完全部会话失效，需重新登录）</summary>
    [HttpPut("password")]
    public async Task<ApiResult<object>> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        await _svc.ChangePasswordAsync(User.GetUserId(), req);
        return ApiOk("密码修改成功，请重新登录");
    }
}
