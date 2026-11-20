using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>
/// 登录安全入口：验证码图（登录页匿名取）、安全设置与日志保留策略（审计管理员维护）。
/// 设置保存即清缓存生效，且保存本身由审计留痕（见 AuditTrail 埋点）。
/// </summary>
[Route("audit")]
public class AuditController : BaseApiController
{
    private readonly LoginSecurityAppService _security;
    private readonly Common.Security.CaptchaService _captcha;
    private readonly OperationLogAppService _logs;
    private readonly AuditCleanupService _cleanup;
    private readonly AuditTrail _trail;

    public AuditController(LoginSecurityAppService security, Common.Security.CaptchaService captcha,
        OperationLogAppService logs, AuditCleanupService cleanup, AuditTrail trail)
    {
        _security = security; _captcha = captcha; _logs = logs; _cleanup = cleanup; _trail = trail;
    }

    /// <summary>获取登录验证码（SVG，一次性答案缓存 5 分钟）</summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    public ApiResult<object> Captcha()
    {
        var (id, svg) = _captcha.Generate();
        return ApiOk<object>(new { captchaId = id, svg });
    }

    /// <summary>读取登录安全设置与日志保留策略（未配置的键返回内置默认值）</summary>
    [HttpGet("settings")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<LoginSecuritySettings>> GetSettings() => ApiOk(await _security.GetAsync());

    /// <summary>保存登录安全设置与日志保留策略（整体覆盖，保存即清缓存立即生效；本操作自动留痕）</summary>
    [HttpPut("settings")]
    [HttpPost("settings")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> SaveSettings([FromBody] LoginSecuritySettings req)
    {
        var before = await _security.GetAsync();
        await _security.SaveAsync(req);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "audit:settings/save", before, req, Ip);
        return ApiOk("保存成功");
    }

    /// <summary>分页查询审计日志（类别/账号/结果/时间区间筛选，仅审计管理员）</summary>
    [HttpGet("log")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Logs([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? category = null, [FromQuery] string? userName = null,
        [FromQuery] int? success = null, [FromQuery] DateTime? begin = null, [FromQuery] DateTime? end = null)
    {
        var page = await _logs.PageAsync(pageIndex, pageSize, category, userName, success, begin, end);
        return ApiOk<object>(page);
    }

    /// <summary>立即执行一轮日志清理（保留策略外的旧日志批量删除，返回删除数）</summary>
    [HttpPost("cleanup")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Cleanup()
    {
        var settings = await _security.GetAsync();
        var deleted = await _cleanup.CleanupAsync(settings.LogRetentionDays);
        return ApiOk<object>(new { deleted });
    }
}

/// <summary>
/// 通知渠道入口（只埋不接线）：SMTP/短信网关配置读写存 sys_site_config，
/// 保存成功即生效落库，发送通道为占位实现（不发真实请求）。
/// </summary>
[Route("notify-channel")]
public class NotifyChannelController : BaseApiController
{
    private readonly NotifyChannelAppService _channel;
    private readonly AuditTrail _trail;

    public NotifyChannelController(NotifyChannelAppService channel, AuditTrail trail)
    {
        _channel = channel; _trail = trail;
    }

    /// <summary>读取通知渠道配置（未配置项返回空串；口子只埋不接线）</summary>
    [HttpGet]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Get() => ApiOk<object>(await _channel.GetAsync());

    /// <summary>保存通知渠道配置（整体覆盖；通道未接线，仅供配置留存；本操作自动留痕）</summary>
    [HttpPut]
    [HttpPost]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Save([FromBody] Dictionary<string, string> configs)
    {
        await _channel.SaveAsync(configs);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "notify:channel/save", null, configs, Ip);
        return ApiOk(NullSmsSender.PlaceholderMessage);
    }
}
