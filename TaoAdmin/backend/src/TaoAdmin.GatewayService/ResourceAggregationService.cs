using System.Text.Json;

namespace TaoAdmin.GatewayService;

/// <summary>资源指标快照（网关侧副本，与各服务 /metrics/resources 的 JSON 结构对齐）。</summary>
public class GatewayResourceSnapshot
{
    public string ServiceName { get; set; } = "";
    public DateTime CollectedAt { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryUsedMB { get; set; }
    public double MemoryTotalMB { get; set; }
    public double MemoryPercent { get; set; }
    public double DiskUsedGB { get; set; }
    public double DiskTotalGB { get; set; }
    public double DiskPercent { get; set; }
    public double NetRxKBps { get; set; }
    public double NetTxKBps { get; set; }
    public bool Available { get; set; } = true;
}

/// <summary>
/// 网关资源聚合（②）：按 Monitor:Services 清单逐个拉取各服务 /metrics/resources，
/// 汇总成一份全服务资源视图；探测失败的服务返回 Available=false 的占位快照，不中断整份聚合。
/// 网关保持独立部署（不引用 Common），快照结构走 JSON 对齐。
/// </summary>
public class ResourceAggregationService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<ResourceAggregationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>聚合全部服务资源快照（单服务超时 3 秒，失败占位不拖垮整份）。</summary>
    public virtual async Task<List<GatewayResourceSnapshot>> AggregateAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<GatewayResourceSnapshot>();
        foreach (var (name, url) in ConfiguredServices())
        {
            var snapshot = await FetchAsync(name, url, cancellationToken).ConfigureAwait(false);
            if (snapshot != null)
            {
                snapshot.ServiceName = name;
                result.Add(snapshot);
            }
            else
            {
                result.Add(new GatewayResourceSnapshot
                {
                    ServiceName = name,
                    CollectedAt = DateTime.Now,
                    Available = false
                });
            }
        }
        return result;
    }

    /// <summary>配置的服务清单（Monitor:Services：名称 -> 地址）。</summary>
    public List<(string Name, string Url)> ConfiguredServices()
        => config.GetSection("Monitor:Services").GetChildren()
            .Select(section => (section.Key!, section.Value!))
            .Where(pair => !string.IsNullOrEmpty(pair.Item2))
            .ToList();

    private async Task<GatewayResourceSnapshot?> FetchAsync(string name, string url, CancellationToken ct)
    {
        try
        {
            var http = httpFactory.CreateClient("resource-aggregate");
            http.Timeout = TimeSpan.FromSeconds(3);
            using var resp = await http.GetAsync(url.TrimEnd('/') + "/metrics/resources", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("资源指标拉取失败：{Service} HTTP {Status}", name, (int)resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<GatewayResourceSnapshot>(JsonOptions, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("资源指标拉取异常：{Service}，{Message}", name, ex.Message);
            return null;
        }
    }
}
