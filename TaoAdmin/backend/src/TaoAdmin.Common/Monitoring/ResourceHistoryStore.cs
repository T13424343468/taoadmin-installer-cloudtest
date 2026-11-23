using System.Collections.Concurrent;

namespace TaoAdmin.Common.Monitoring;

/// <summary>
/// 资源采样历史（进程内环形缓冲）：每服务保留最近 capacity 条采样，
/// 供监控页「近期趋势」查询；重启清零属预期（趋势看近期，不替代历史库）。
/// </summary>
public class ResourceHistoryStore
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ResourceSnapshot>> _byService = new(StringComparer.OrdinalIgnoreCase);

    public ResourceHistoryStore(int capacity = 240)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "容量至少 1 条");
        _capacity = capacity;
    }

    /// <summary>追加一条采样；超容量淘汰最旧。</summary>
    public void Add(ResourceSnapshot snapshot)
    {
        var queue = _byService.GetOrAdd(snapshot.ServiceName, _ => new ConcurrentQueue<ResourceSnapshot>());
        queue.Enqueue(snapshot);
        while (queue.Count > _capacity)
            queue.TryDequeue(out _);
    }

    /// <summary>该服务最新一条采样（无采样返回 null）。</summary>
    public ResourceSnapshot? LatestOf(string serviceName)
        => _byService.TryGetValue(serviceName, out var queue) && queue.TryPeek(out _)
            ? queue.Reverse().First()
            : null;

    /// <summary>该服务自 from 起的采样（旧→新，供趋势图）。</summary>
    public List<ResourceSnapshot> Since(string serviceName, DateTime from)
        => !_byService.TryGetValue(serviceName, out var queue)
            ? []
            : queue.Where(s => s.CollectedAt >= from).ToList();

    /// <summary>全部服务最新快照（无采样的服务不在列）。</summary>
    public List<ResourceSnapshot> LatestAll()
        => _byService.Keys.Select(serviceName => LatestOf(serviceName))
            .OfType<ResourceSnapshot>()
            .ToList();

    /// <summary>已登记采样历史的服务名清单。</summary>
    public IReadOnlyCollection<string> Services => _byService.Keys.ToList();
}
