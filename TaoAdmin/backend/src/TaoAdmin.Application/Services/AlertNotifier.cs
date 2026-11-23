using SqlSugar;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 告警通知器：按事件类型加载启用的告警规则，向规则配置的渠道发送告警并落发送记录。
/// 发送失败记录失败原因，不抛出、不阻塞主流程（任务执行/健康探测不受影响）。
/// 触发口径：事件连续发生次数 currentCount 达到规则 Threshold 时触发（恢复后重新计数）。
/// </summary>
public class AlertNotifier
{
    /// <summary>支持的通知渠道</summary>
    public static readonly string[] KnownChannels = { "email", "dingtalk" };

    private readonly ISqlSugarClient _db;
    private readonly SnowflakeIdGenerator _snowflake;
    private readonly IEmailSender _email;
    private readonly DingTalkSender _dingtalk;

    public AlertNotifier(ISqlSugarClient db, SnowflakeIdGenerator snowflake,
        IEmailSender email, DingTalkSender dingtalk)
    {
        _db = db;
        _snowflake = snowflake;
        _email = email;
        _dingtalk = dingtalk;
    }

    /// <summary>
    /// 触发一次事件告警：命中启用规则（事件类型匹配且阈值已达）后逐渠道发送，返回成功发送数。
    /// </summary>
    public async Task<int> NotifyAsync(string eventType, string title, string content, int currentCount)
    {
        var rules = await _db.Queryable<SysAlertRule>()
            .Where(r => r.Enabled && r.EventType == eventType && r.Threshold <= currentCount)
            .ToListAsync();
        var sent = 0;
        foreach (var rule in rules)
        {
            foreach (var channel in ParseChannels(rule.Channels))
            {
                var (ok, msg, target) = await SendAsync(channel, title, content);
                await RecordAsync(channel, target, title, content, ok, msg);
                if (ok)
                    sent++;
            }
        }
        return sent;
    }

    /// <summary>发送测试消息（不经规则过滤，直接走指定渠道），返回发送结果。</summary>
    public async Task<(bool Ok, string Message)> SendTestAsync(string channel)
    {
        var title = "【TaoAdmin】测试消息";
        var content = $"通知渠道接线验证：{channel} 渠道测试消息，发送时间 {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        if (!KnownChannels.Contains(channel))
            return (false, $"未知渠道：{channel}");
        var (ok, msg, target) = await SendAsync(channel, title, content);
        await RecordAsync(channel, target, title, content, ok, msg);
        return (ok, msg);
    }

    public static string[] ParseChannels(string channels)
        => channels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<(bool Ok, string Message, string Target)> SendAsync(string channel, string title, string content)
    {
        try
        {
            return channel switch
            {
                "email" => await SendEmailAsync(title, content),
                "dingtalk" => await SendDingTalkAsync(title, content),
                _ => (false, $"未知渠道：{channel}", string.Empty)
            };
        }
        catch (Exception ex)
        {
            return (false, $"渠道 {channel} 发送异常：{ex.Message}", string.Empty);
        }
    }

    private async Task<(bool Ok, string Message, string Target)> SendEmailAsync(string title, string content)
    {
        var receivers = await GetConfigAsync("notify_alert_receivers");
        if (string.IsNullOrWhiteSpace(receivers))
            return (false, "告警接收邮箱未配置（notify_alert_receivers）", string.Empty);
        var result = await _email.SendAsync(receivers, title, content);
        return (result.Ok, result.Message, receivers);
    }

    private async Task<(bool Ok, string Message, string Target)> SendDingTalkAsync(string title, string content)
    {
        var result = await _dingtalk.SendAsync(title, content);
        return (result.Ok, result.Message, "dingtalk-webhook");
    }

    private async Task RecordAsync(string channel, string target, string subject, string content,
        bool ok, string message)
    {
        await _db.Insertable(new SysNotifyLog
        {
            Id = _snowflake.NextId(),
            Channel = channel,
            Target = target,
            Subject = subject,
            Content = content.Length > 1000 ? content[..1000] : content,
            Success = ok,
            Error = ok ? null : (message.Length > 500 ? message[..500] : message)
        }).ExecuteCommandAsync();
    }

    private async Task<string> GetConfigAsync(string key)
    {
        var row = await _db.Queryable<SysSiteConfig>()
            .FirstAsync(c => c.IsDeleted == false && c.ConfigKey == key);
        return row?.ConfigValue ?? string.Empty;
    }
}
