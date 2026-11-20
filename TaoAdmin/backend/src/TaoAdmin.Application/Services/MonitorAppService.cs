using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
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

    /// <summary>分页查询本地 JSON 日志（倒序）</summary>
    public Task<(long Total, List<LogItemDto> Items)> GetLogsAsync(
        int pageIndex, int pageSize, string? level, string? service, string? keyword)
    {
        var dir = _config["Monitor:LogDir"] ?? "logs";
        var lines = new List<LogItemDto>();

        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl").OrderByDescending(f => f))
            {
                foreach (var line in File.ReadLines(file))
                {
                    var item = ParseLine(line);
                    if (item == null) continue;
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

    /// <summary>解析 Serilog JSON 日志行（Compact 格式）</summary>
    private static LogItemDto? ParseLine(string line)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(line).RootElement;
            var level = json.TryGetProperty("Level", out var lv) ? ShortLevel(lv.GetString() ?? "") : "INFO";
            var ts = json.TryGetProperty("@t", out var t) ? t.GetString() : null;
            var msg = json.TryGetProperty("@m", out var m) ? m.GetString() : "";
            var ex = json.TryGetProperty("@x", out var x) && x.ValueKind != System.Text.Json.JsonValueKind.Null ? x.GetString() : null;
            var thread = json.TryGetProperty("ThreadId", out var th) ? th.GetString() : null;
            var traceId = json.TryGetProperty("TraceId", out var tr) ? tr.GetString() : null;
            var service = json.TryGetProperty("Service", out var sv) ? sv.GetString() : Path.GetFileNameWithoutExtension(json.TryGetProperty("@l", out _) ? "app" : "app");
            var logger = json.TryGetProperty("@l", out var lg) ? lg.GetString() : null;
            return new LogItemDto
            {
                id = $"{ts}-{thread}",
                timestamp = ts ?? string.Empty,
                level = level,
                service = service ?? "unknown",
                traceId = traceId,
                thread = thread,
                logger = logger,
                message = msg ?? string.Empty,
                exception = ex
            };
        }
        catch
        {
            return null;
        }
    }

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
