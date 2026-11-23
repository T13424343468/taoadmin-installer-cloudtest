using System.Collections.Concurrent;

namespace TaoAdmin.Common.Alerting;

/// <summary>
/// 默认告警事件发布器：记日志 + 进程内环形缓冲留存最近事件（监控页「近期告警」数据源）。
/// 告警中心接线后可替换为真实投递实现；本实现保证发布不抛异常，不反噬业务流程。
/// </summary>
public class InMemoryAlertEventPublisher : IAlertEventPublisher
{
    private readonly ConcurrentQueue<AlertEvent> _buffer = new();
    private readonly int _capacity;
    private readonly Action<AlertEvent>? _sink;

    /// <summary>注入 sink 便于测试观察发布动作；生产默认仅入缓冲。</summary>
    public InMemoryAlertEventPublisher(int capacity = 200, Action<AlertEvent>? sink = null)
    {
        _capacity = capacity;
        _sink = sink;
    }

    public Task PublishAsync(AlertEvent evt, CancellationToken cancellationToken = default)
    {
        try
        {
            _buffer.Enqueue(evt);
            // 环形缓冲：超容量淘汰最旧事件，监控页只看最近一段
            while (_buffer.Count > _capacity)
                _buffer.TryDequeue(out _);
            _sink?.Invoke(evt);
        }
        catch
        {
            // 告警是旁路动作：缓冲异常不反噬业务
        }
        return Task.CompletedTask;
    }

    /// <summary>最近事件（新→旧）；type 非空时按类型过滤。</summary>
    public IReadOnlyList<AlertEvent> Recent(int count, string? type = null)
    {
        IEnumerable<AlertEvent> query = _buffer.Reverse();
        if (!string.IsNullOrEmpty(type))
            query = query.Where(e => e.Type == type);
        return query.Take(count).ToList();
    }
}
