using Microsoft.Extensions.Configuration;
using TaoAdmin.Application.Dtos;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 监控中心应用服务：服务健康探测 + 本地日志文件查询。
/// 沙箱/本地模式无 Nacos 与 ES，服务清单来自配置并做 HTTP 健康探测；
/// 日志读取各服务 Serilog JSON 文件（logs/ 目录），生产环境可替换为 ES 查询实现。
/// </summary>
public class MonitorAppService
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpFactory;

    /// <summary>受监控服务清单：名称 -> (显示名, 描述, 健康检查地址)</summary>
    private static readonly (string Name, string Display, string Desc, string? HealthUrl)[] KnownServices =
    {
        ("taoadmin-gateway", "API 网关", "统一入口：鉴权、限流、路由", "gateway"),
        ("taoadmin-identity", "权限服务", "用户/角色/菜单/接口权限", "identity"),
        ("taoadmin-task", "定时任务服务", "Quartz 调度中心", "task")
    };

    public MonitorAppService(IConfiguration config, IHttpClientFactory httpFactory)
    {
        _config = config; _httpFactory = httpFactory;
    }

    /// <summary>探测全部服务健康状态</summary>
    public async Task<List<ServiceItemDto>> GetServicesAsync()
    {
        var result = new List<ServiceItemDto>();
        foreach (var (name, display, desc, key) in KnownServices)
        {
            var url = _config[$"Monitor:Services:{key}"];
            var item = new ServiceItemDto
            {
                serviceName = name,
                displayName = display,
                description = desc,
                version = "1.0.0"
            };
            if (string.IsNullOrEmpty(url))
            {
                item.status = "DOWN";
            }
            else
            {
                var up = await PingAsync(url);
                item.status = up ? "UP" : "DOWN";
                item.instances = up ? 1 : 0;
                item.lastHealthCheck = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (up)
                {
                    var uri = new Uri(url);
                    item.instanceList.Add(new ServiceInstanceDto
                    {
                        instanceId = $"{name}-{uri.Port}",
                        host = uri.Host,
                        port = uri.Port,
                        status = "UP",
                        lastHeartbeat = item.lastHealthCheck
                    });
                }
            }
            result.Add(item);
        }
        return result;
    }

    /// <summary>分页查询本地 JSON 日志（倒序）：聚合目录下全部服务的日志文件，按服务名过滤。</summary>
    public Task<(long Total, List<LogItemDto> Items)> GetLogsAsync(
        int pageIndex, int pageSize, string? level, string? service, string? keyword)
    {
        var dir = _config["Monitor:LogDir"] ?? "logs";
        var lines = new List<LogItemDto>();

        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl").OrderByDescending(f => f))
            {
                // 服务来源：优先取行内 Service 属性，取不到时用文件名（各服务分文件写日志，如 gateway.jsonl）
                var fallbackService = Path.GetFileNameWithoutExtension(file);
                foreach (var line in File.ReadLines(file))
                {
                    var parsed = SerilogLineParser.Parse(line);
                    if (parsed == null) continue;
                    var item = ToDto(parsed, fallbackService);
                    if (!string.IsNullOrEmpty(level) && !string.Equals(item.level, level, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(service) && !item.service.Contains(service, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(keyword) && !item.message.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;
                    lines.Add(item);
                }
            }
        }

        var total = (long)lines.Count;
        var items = lines.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((total, items));
    }

    private static LogItemDto ToDto(SerilogLogEntry parsed, string fallbackService) => new()
    {
        id = $"{parsed.Timestamp}-{parsed.Thread}",
        timestamp = parsed.Timestamp,
        level = parsed.Level,
        service = parsed.Service ?? fallbackService,
        traceId = parsed.TraceId,
        thread = parsed.Thread,
        logger = parsed.Logger,
        message = parsed.Message,
        exception = parsed.Exception
    };

    private static string ShortLevel(string raw) => raw switch
    {
        "DBG" or "DEBUG" or "Verbose" or "Debug" => "DEBUG",
        "WRN" or "WARNING" or "Warning" => "WARN",
        "ERR" or "ERROR" or "Fatal" or "FTL" => "ERROR",
        _ => "INFO"
    };

    private static async Task<bool> PingAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var resp = await http.GetAsync(url.TrimEnd('/') + "/health");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
