using System.Text.RegularExpressions;

namespace TaoAdmin.Application.Services;

/// <summary>
/// Serilog Compact JSON 日志行解析器：把一行 JSONL 渲染成日志中心展示项。
/// 兼容两种行格式：带渲染后消息的 @m（RenderedCompactJsonFormatter），
/// 与消息模板 @mt + 参数（CompactJsonFormatter，本项目各服务实际写法）。
/// </summary>
public static class SerilogLineParser
{
    /// <summary>Serilog 消息模板参数占位：{Name} 或 {Name:format}，含转义花括号 {{ }}。</summary>
    private static readonly Regex TemplateParamRegex = new(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?::[^}]+)?\}", RegexOptions.Compiled);

    /// <summary>解析一行日志；非 JSON 或缺时间戳的行返回 null（调用方跳过）。</summary>
    public static SerilogLogEntry? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            var json = doc.RootElement;
            if (!json.TryGetProperty("@t", out var tEl)) return null;
            var ts = tEl.GetString();
            if (string.IsNullOrEmpty(ts)) return null;

            var level = json.TryGetProperty("@l", out var lv) ? ShortLevel(lv.GetString() ?? "") : "INFO";
            var ex = json.TryGetProperty("@x", out var x) && x.ValueKind != System.Text.Json.JsonValueKind.Null ? x.GetString() : null;
            var logger = json.TryGetProperty("SourceContext", out var lg) ? lg.GetString() : null;
            var service = json.TryGetProperty("Service", out var sv) ? sv.GetString() : null;
            var thread = json.TryGetProperty("ThreadId", out var th) ? th.GetString() : null;
            var traceId = json.TryGetProperty("TraceId", out var tr) ? tr.GetString() : null;

            // 消息：有 @m（已渲染）直接用；否则用 @mt 模板按属性渲染（本项目 CompactJsonFormatter 写 @mt）
            string message;
            if (json.TryGetProperty("@m", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                message = m.GetString() ?? string.Empty;
            }
            else if (json.TryGetProperty("@mt", out var mt))
            {
                message = RenderTemplate(mt.GetString() ?? string.Empty, json);
            }
            else
            {
                message = string.Empty;
            }

            return new SerilogLogEntry(ts, level, service, logger, thread, traceId, message, ex);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>用日志行上的属性渲染消息模板：命名参数按名取值，缺参保留原占位。</summary>
    private static string RenderTemplate(string template, System.Text.Json.JsonElement json)
    {
        return TemplateParamRegex.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            return json.TryGetProperty(name, out var val)
                ? val.ValueKind == System.Text.Json.JsonValueKind.String
                    ? val.GetString() ?? string.Empty
                    : val.ToString()
                : match.Value;
        });
    }

    private static string ShortLevel(string raw) => raw switch
    {
        "DBG" or "DEBUG" or "Verbose" or "Debug" => "DEBUG",
        "WRN" or "WARNING" or "Warning" => "WARN",
        "ERR" or "ERROR" or "Fatal" or "FTL" => "ERROR",
        _ => "INFO"
    };
}

/// <summary>解析后的单行日志（与日志中心 DTO 字段一一对应）。</summary>
public record SerilogLogEntry(
    string Timestamp,
    string Level,
    string? Service,
    string? Logger,
    string? Thread,
    string? TraceId,
    string Message,
    string? Exception);
