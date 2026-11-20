using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Common.Snowflake;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 关键数据留痕（不可抵赖）：敏感操作写「改了什么前后值」JSON 进 sys_operation_log.Detail。
/// 密码类字段一律掩码 ***；留痕失败只记警告不阻塞业务（与操作日志中间件同口径）。
/// 埋点为显式调用，非中间件路径——只埋敏感写操作，普通查询不留痕。
/// </summary>
public class AuditTrail(ISqlSugarClient db, SnowflakeIdGenerator snowflake, ILogger<AuditTrail> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>写一条留痕：前后值对象分别序列化，敏感键自动掩码。</summary>
    public async Task WriteAsync(long userId, string userName, long tenantId, string action,
        object? before, object? after, string ip = "")
    {
        try
        {
            var detail = JsonSerializer.Serialize(new
            {
                action,
                before = before == null ? null : Mask(ToJson(before)),
                after = after == null ? null : Mask(ToJson(after))
            }, JsonOpts);
            await db.Insertable(new SysOperationLog
            {
                Id = snowflake.NextId(),
                UserId = userId,
                UserName = userName,
                TenantId = tenantId,
                Category = "audit",
                Method = "AUDIT",
                Path = action,
                StatusCode = 200,
                Success = 1,
                Ip = ip,
                Detail = detail
            }).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            // 留痕失败不阻塞业务：与操作日志中间件同口径，只记警告
            logger.LogWarning(ex, "审计留痕写入失败 action={Action} user={User}", action, userName);
        }
    }

    private static string ToJson(object o) => JsonSerializer.Serialize(o, JsonOpts);

    /// <summary>敏感字段掩码：键名含 password/secret/token 的值替换为 ***（防明文进审计）。</summary>
    private static string Mask(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var masked = MaskNode(doc.RootElement);
            return masked.GetRawText();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static JsonElement MaskNode(JsonElement e)
    {
        using var ms = new System.IO.MemoryStream();
        using (var w = new System.Text.Json.Utf8JsonWriter(ms))
        {
            WriteNode(w, e);
            w.Flush();
        }
        return JsonDocument.Parse(ms.ToArray()).RootElement;
    }

    private static void WriteNode(System.Text.Json.Utf8JsonWriter w, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject())
                {
                    w.WritePropertyName(p.Name);
                    if (p.Value.ValueKind == JsonValueKind.String &&
                        (p.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)))
                        w.WriteStringValue("***");
                    else
                        WriteNode(w, p.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in e.EnumerateArray()) WriteNode(w, item);
                w.WriteEndArray();
                break;
            default:
                e.WriteTo(w);
                break;
        }
    }
}
