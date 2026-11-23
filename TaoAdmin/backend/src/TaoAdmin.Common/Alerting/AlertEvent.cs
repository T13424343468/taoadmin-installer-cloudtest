namespace TaoAdmin.Common.Alerting;

/// <summary>告警严重级别常量（与告警中心约定口径）。</summary>
public static class AlertSeverity
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
}

/// <summary>
/// 标准化告警事件（跨模块统一抛出口）：资源监控、后续告警接线方共用同一事件结构。
/// 事件只描述事实（谁、何时、发生了什么、级别与附加数据），投递渠道由告警中心决定。
/// </summary>
public record AlertEvent(
    string EventId,
    string Type,
    string Severity,
    string Source,
    string Title,
    string Content,
    DateTime OccurredAt,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// 告警事件发布口（对接点）：业务侧只依赖本接口抛事件，不关心投递渠道。
/// 告警中心接线时替换/包装实现（如写告警库、推通知渠道），业务代码零改动。
/// </summary>
public interface IAlertEventPublisher
{
    /// <summary>发布一条告警事件（实现须自行兜底异常，不反噬业务流程）。</summary>
    Task PublishAsync(AlertEvent evt, CancellationToken cancellationToken = default);
}
