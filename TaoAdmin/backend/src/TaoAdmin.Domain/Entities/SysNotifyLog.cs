using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>
/// 通知发送记录（sys_notify_log）：每次告警/测试消息发送落一条，成功失败均留痕。
/// </summary>
[SugarTable("sys_notify_log", TableDescription = "通知发送记录")]
public class SysNotifyLog : BaseEntity
{
    /// <summary>渠道：email / dingtalk</summary>
    [SugarColumn(Length = 20, ColumnDescription = "渠道")]
    public string Channel { get; set; } = string.Empty;

    /// <summary>接收目标（邮箱地址 / Webhook 前缀）</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "接收目标")]
    public string? Target { get; set; }

    /// <summary>主题</summary>
    [SugarColumn(Length = 200, IsNullable = true, ColumnDescription = "主题")]
    public string? Subject { get; set; }

    /// <summary>内容摘要</summary>
    [SugarColumn(Length = 1000, IsNullable = true, ColumnDescription = "内容")]
    public string? Content { get; set; }

    /// <summary>是否发送成功</summary>
    [SugarColumn(ColumnDescription = "是否成功")]
    public bool Success { get; set; }

    /// <summary>失败原因（成功为空）</summary>
    [SugarColumn(Length = 500, IsNullable = true, ColumnDescription = "失败原因")]
    public string? Error { get; set; }
}
