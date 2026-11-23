namespace TaoAdmin.Common.Monitoring;

/// <summary>资源异常阈值（百分比，0~100）：CPU / 内存 / 磁盘。</summary>
public record ResourceThresholds(double CpuPercent, double MemoryPercent, double DiskPercent);

/// <summary>单指标评估结论：是否超限、超限级别与文案。</summary>
public record MetricEvaluation(string Metric, double Value, double Threshold, bool Over, string Severity);

/// <summary>
/// 资源阈值评估（纯函数，无状态）：逐指标比对阈值，返回全部指标结论。
/// 状态机（是否已发过告警）由调用方维护，见 <see cref="EvaluateTransition"/>。
/// </summary>
public static class ResourceThresholdEvaluator
{
    /// <summary>逐指标评估快照（CPU / 内存 / 磁盘）。</summary>
    public static List<MetricEvaluation> Evaluate(ResourceSnapshot snapshot, ResourceThresholds thresholds)
    {
        return
        [
            EvaluateOne("cpu", snapshot.CpuPercent, thresholds.CpuPercent),
            EvaluateOne("memory", snapshot.MemoryPercent, thresholds.MemoryPercent),
            EvaluateOne("disk", snapshot.DiskPercent, thresholds.DiskPercent)
        ];
    }

    private static MetricEvaluation EvaluateOne(string metric, double value, double threshold)
    {
        var over = value > threshold;
        // 超阈值 10 个百分点以上算严重，其余为警告
        var severity = over && value > threshold + 10 ? Alerting.AlertSeverity.Critical : Alerting.AlertSeverity.Warning;
        return new MetricEvaluation(metric, Math.Round(value, 1), threshold, over, severity);
    }

    /// <summary>
    /// 状态迁移：正常→超限发告警事件；持续超限不重复发；超限→恢复发恢复事件；持续正常返回 null。
    /// 返回（事件, 迁移后是否处于告警态）。
    /// </summary>
    public static (Alerting.AlertEvent? Event, bool Alerting) EvaluateTransition(
        ResourceSnapshot snapshot, ResourceThresholds thresholds, bool wasAlerting)
    {
        var metrics = Evaluate(snapshot, thresholds);
        var over = metrics.Any(m => m.Over);
        if (over && !wasAlerting)
        {
            var worst = metrics.Where(m => m.Over).OrderByDescending(m => m.Severity == Alerting.AlertSeverity.Critical).First();
            var detail = string.Join("、", metrics.Where(m => m.Over)
                .Select(m => $"{MetricLabel(m.Metric)} {m.Value}%（阈值 {m.Threshold}%）"));
            var evt = new Alerting.AlertEvent(
                EventId: Guid.NewGuid().ToString("N"),
                Type: "resource.threshold",
                Severity: worst.Severity,
                Source: snapshot.ServiceName,
                Title: $"{snapshot.ServiceName} 资源占用超阈值",
                Content: $"{snapshot.ServiceName} {detail}，采样时间 {snapshot.CollectedAt:yyyy-MM-dd HH:mm:ss}",
                OccurredAt: snapshot.CollectedAt,
                Data: metrics.ToDictionary(m => m.Metric, m => m.Value.ToString("0.0")));
            return (evt, true);
        }
        if (!over && wasAlerting)
        {
            var evt = new Alerting.AlertEvent(
                EventId: Guid.NewGuid().ToString("N"),
                Type: "resource.recovered",
                Severity: Alerting.AlertSeverity.Info,
                Source: snapshot.ServiceName,
                Title: $"{snapshot.ServiceName} 资源占用已恢复",
                Content: $"{snapshot.ServiceName} 全部资源指标回落至阈值内，采样时间 {snapshot.CollectedAt:yyyy-MM-dd HH:mm:ss}",
                OccurredAt: snapshot.CollectedAt);
            return (evt, false);
        }
        return (null, over);
    }

    internal static string MetricLabel(string metric) => metric switch
    {
        "cpu" => "CPU",
        "memory" => "内存",
        "disk" => "磁盘",
        _ => metric
    };
}
