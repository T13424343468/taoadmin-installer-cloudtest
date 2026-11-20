using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>在线用户入口：当前登录名单与强制下线。账本在 OnlineSessionStore。</summary>
[Route("online-users")]
public class OnlineUserController : BaseApiController
{
    private readonly OnlineSessionStore _sessions;

    public OnlineUserController(OnlineSessionStore sessions) => _sessions = sessions;

    /// <summary>当前在线用户名单（按登录时间倒序）</summary>
    [HttpGet]
    [Permission("monitor:online:list")]
    public ApiResult<List<OnlineSession>> List() => ApiOk(_sessions.List());

    /// <summary>强制下线指定会话（该令牌即刻失效，用户下次请求即被请回登录页）</summary>
    [HttpDelete("{sessionId}")]
    [Permission("monitor:online:kick")]
    public ApiResult<object> Kick(string sessionId)
        => ApiOk(_sessions.Revoke(sessionId) ? "该会话已被强制下线" : "会话不存在或已下线");
}
