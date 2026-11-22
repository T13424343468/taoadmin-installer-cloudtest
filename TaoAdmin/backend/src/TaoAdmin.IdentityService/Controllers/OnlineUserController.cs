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

    /// <summary>批量强制下线（可容错逐条执行，返回实际下线数与失败明细）</summary>
    [HttpPost("batch-kick")]
    [Permission("monitor:online:kick")]
    public ApiResult<object> BatchKick([FromBody] BatchKickRequest req)
    {
        var kicked = 0;
        var failed = new List<string>();
        foreach (var sessionId in req.sessionIds.Distinct())
            if (_sessions.Revoke(sessionId)) kicked++;
            else failed.Add(sessionId); // 会话不存在或已下线，逐条汇报不中断
        return ApiOk<object>(new { kicked, failed }, "批量下线完成");
    }
}

/// <summary>批量强制下线请求：会话 ID 列表</summary>
public class BatchKickRequest
{
    /// <summary>要下线的会话 ID</summary>
    public List<string> sessionIds { get; set; } = new();
}
