using System.Text;
using System.Text.Json;
using SqlSugar;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 钉钉群机器人真实发送实现：走自定义机器人 Webhook（text 消息）。
/// Webhook 运行时从 sys_site_config 读取（notify_dingtalk_webhook 键），代码不落任何真实地址。
/// HttpClient 可注入（测试用假 Handler 替代真实网络）。
/// </summary>
public class DingTalkSender
{
    private readonly ISqlSugarClient _db;
    private readonly HttpClient _http;

    public DingTalkSender(ISqlSugarClient db, HttpClient? http = null)
    {
        _db = db;
        _http = http ?? new HttpClient();
    }

    /// <summary>发送钉钉文本消息：Webhook 未配置/接口异常/errcode 非零均返回失败原因，不抛出。</summary>
    public async Task<(bool Ok, string Message)> SendAsync(string title, string content)
    {
        var webhook = await LoadWebhookAsync();
        if (string.IsNullOrWhiteSpace(webhook))
            return (false, "钉钉 Webhook 未配置");

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                msgtype = "text",
                text = new { content = $"{title}\n{content}" }
            });
            var resp = await _http.PostAsync(webhook,
                new StringContent(payload, Encoding.UTF8, "application/json"));
            if (!resp.IsSuccessStatusCode)
                return (false, $"钉钉接口返回 HTTP {(int)resp.StatusCode}");
            var body = await resp.Content.ReadAsStringAsync();
            return body.Contains("\"errcode\":0")
                ? (true, "发送成功")
                : (false, $"钉钉返回：{body}");
        }
        catch (Exception ex)
        {
            return (false, $"钉钉发送失败：{ex.Message}");
        }
    }

    private async Task<string> LoadWebhookAsync()
    {
        var row = await _db.Queryable<SysSiteConfig>()
            .FirstAsync(c => c.IsDeleted == false && c.ConfigKey == "notify_dingtalk_webhook");
        return row?.ConfigValue ?? string.Empty;
    }
}
