using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

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
    private readonly AuditLogExcelAppService _excel;

    public AuditController(LoginSecurityAppService security, Common.Security.CaptchaService captcha,
        OperationLogAppService logs, AuditCleanupService cleanup, AuditTrail trail,
        AuditLogExcelAppService excel)
    {
        _security = security; _captcha = captcha; _logs = logs; _cleanup = cleanup; _trail = trail;
        _excel = excel;
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

    /// <summary>分页查询审计日志（类别/账号/结果/时间区间筛选，仅审计管理员；cursor=上一页 NextCursor 时走游标深翻页）</summary>
    [HttpGet("log")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Logs([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? category = null, [FromQuery] string? userName = null,
        [FromQuery] int? success = null, [FromQuery] DateTime? begin = null, [FromQuery] DateTime? end = null,
        [FromQuery] string? cursor = null)
    {
        var page = await _logs.PageAsync(pageIndex, pageSize, category, userName, success, begin, end, cursor);
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

    /// <summary>批量删除审计日志（物理删除，返回实际删除数；本操作自动留痕）</summary>
    [HttpPost("log/batch-delete")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> BatchDeleteLogs([FromBody] BatchIdsRequest req)
    {
        var deleted = await _logs.BatchDeleteAsync(req.Ids);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "audit:log/batch-delete",
            null, new { count = req.Ids.Count, deleted }, Ip);
        return ApiOk<object>(new { deleted }, "批量删除完成");
    }

    /// <summary>按当前筛选条件导出审计日志为 xlsx（⑮；上限 5 万条，超出请收窄筛选；本操作自动留痕）</summary>
    [HttpGet("log/export")]
    [Permission("sys:audit:manage")]
    public async Task<IActionResult> ExportLogs([FromQuery] string? category = null, [FromQuery] string? userName = null,
        [FromQuery] int? success = null, [FromQuery] DateTime? begin = null, [FromQuery] DateTime? end = null)
    {
        var (content, count) = await _excel.ExportAsync(category, userName, success, begin, end);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "audit:log/export",
            null, new { category, userName, success, begin, end, exported = count }, Ip);
        return File(content,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"audit-log-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }
}

/// <summary>
/// 通知渠道入口：SMTP/钉钉 Webhook/告警接收人配置读写存 sys_site_config，
/// 发送通道已接线（SmtpEmailSender 真实 SMTP / DingTalkSender 真实 Webhook），
/// 支持发送测试消息验证接线，发送成功失败均落 sys_notify_log。
/// </summary>
[Route("notify-channel")]
public class NotifyChannelController : BaseApiController
{
    private readonly NotifyChannelAppService _channel;
    private readonly AlertNotifier _notifier;
    private readonly ISqlSugarClient _db;
    private readonly AuditTrail _trail;

    public NotifyChannelController(NotifyChannelAppService channel, AlertNotifier notifier,
        ISqlSugarClient db, AuditTrail trail)
    {
        _channel = channel; _notifier = notifier; _db = db; _trail = trail;
    }

    /// <summary>读取通知渠道配置（未配置项返回空串）</summary>
    [HttpGet]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Get() => ApiOk<object>(await _channel.GetAsync());

    /// <summary>保存通知渠道配置（整体覆盖，保存即生效；本操作自动留痕）</summary>
    [HttpPut]
    [HttpPost]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Save([FromBody] Dictionary<string, string> configs)
    {
        await _channel.SaveAsync(configs);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "notify:channel/save", null, configs, Ip);
        return ApiOk("保存成功");
    }

    /// <summary>发送测试消息验证接线（channel=email/dingtalk；结果落发送记录；本操作自动留痕）</summary>
    [HttpPost("test")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Test([FromBody] NotifyTestRequest req)
    {
        var (ok, msg) = await _notifier.SendTestAsync(req.channel);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "notify:channel/test", null,
            new { req.channel, ok }, Ip);
        return ok ? ApiOk<object>(msg) : ApiFail<object>(msg);
    }

    /// <summary>通知发送记录（分页倒序，成功失败均留痕）</summary>
    [HttpGet("logs")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Logs([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.Queryable<TaoAdmin.Domain.Entities.SysNotifyLog>().Where(l => l.IsDeleted == false);
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(l => l.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return ApiOk<object>(new
        {
            total,
            pageIndex,
            pageSize,
            items = rows.Select(l => new
            {
                id = l.Id.ToString(),
                channel = l.Channel,
                target = l.Target,
                subject = l.Subject,
                success = l.Success,
                error = l.Error,
                createdAt = l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            })
        });
    }
}

/// <summary>测试消息发送请求</summary>
public class NotifyTestRequest
{
    /// <summary>渠道：email / dingtalk</summary>
    public string channel { get; set; } = string.Empty;
}

/// <summary>
/// 告警规则入口：配置哪些事件触发告警（SERVICE_DOWN / JOB_FAIL）、连续失败阈值与通知渠道。
/// </summary>
[Route("alert-rules")]
public class AlertRuleController : BaseApiController
{
    private readonly AlertRuleAppService _rules;
    private readonly AuditTrail _trail;

    public AlertRuleController(AlertRuleAppService rules, AuditTrail trail)
    {
        _rules = rules; _trail = trail;
    }

    /// <summary>告警规则清单</summary>
    [HttpGet]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Get() => ApiOk<object>(await _rules.ListAsync());

    /// <summary>新增或更新告警规则（本操作自动留痕）</summary>
    [HttpPost]
    [HttpPut]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Save([FromBody] AlertRuleSaveRequest req)
    {
        await _rules.SaveAsync(req);
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "notify:alert-rule/save", null, req, Ip);
        return ApiOk("保存成功");
    }

    /// <summary>删除告警规则（软删除；本操作自动留痕）</summary>
    [HttpDelete("{id}")]
    [Permission("sys:audit:manage")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _rules.DeleteAsync(long.Parse(id));
        await _trail.WriteAsync(User.GetUserId(), User.GetUserName(), 0, "notify:alert-rule/delete", null,
            new { id }, Ip);
        return ApiOk("删除成功");
    }
}
