using TaoAdmin.Application.Dtos;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 服务下线告警评估器：对健康探测结果做状态迁移判断，
/// 服务从可用转为不可用后按连续探测失败次数触发告警（恢复后重新计数）。
/// 通知委托可注入（测试断言触发参数，不依赖真实渠道）。
/// </summary>
public class ServiceDownAlertEvaluator
{
    private readonly Func<string, string, string, int, Task<int>> _notify;

    public ServiceDownAlertEvaluator(Func<string, string, string, int, Task<int>> notify)
    {
        _notify = notify;
    }

    /// <summary>连续探测失败计数（内存态，进程重启后重新累计）</summary>
    internal readonly Dictionary<string, int> DownCounts = new();

    /// <summary>评估一轮探测结果：DOWN 累计计数并触发告警，UP 清零。</summary>
    public async Task EvaluateAsync(IEnumerable<ServiceItemDto> services)
    {
        foreach (var s in services)
        {
            if (s.status != "DOWN")
            {
                DownCounts.Remove(s.serviceName);
                continue;
            }
            var count = DownCounts.TryGetValue(s.serviceName, out var c) ? c + 1 : 1;
            DownCounts[s.serviceName] = count;
            await _notify("SERVICE_DOWN",
                $"服务不可用：{s.displayName}",
                $"服务 {s.serviceName} 已连续 {count} 次健康检查失败，请尽快处理",
                count);
        }
    }
}
