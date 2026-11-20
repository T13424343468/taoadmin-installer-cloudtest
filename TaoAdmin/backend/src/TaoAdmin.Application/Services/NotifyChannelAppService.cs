using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Common.Snowflake;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 通知渠道配置服务（只埋不接线）：SMTP/短信网关配置存 sys_site_config（notify_ 前缀），
/// 保存即落库生效；发送通道为 ISmsSender/IEmailSender 占位实现，不发真实请求。
/// 白名单键：防任意键注入；值不脱敏落审计（由审计侧统一掩码 secret/token 类字段）。
/// </summary>
public class NotifyChannelAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
{
    /// <summary>可配置键白名单（SMTP 服务器/端口/账号/发件人、短信网关/签名）</summary>
    public static readonly string[] KnownKeys =
    {
        "notify_smtp_host", "notify_smtp_port", "notify_smtp_user", "notify_smtp_from",
        "notify_sms_gateway", "notify_sms_sign"
    };

    /// <summary>读取通知渠道配置（未配置项返回空串）</summary>
    public async Task<Dictionary<string, string>> GetAsync()
    {
        var known = KnownKeys.ToDictionary(k => k, _ => string.Empty);
        var rows = await db.Queryable<SysSiteConfig>()
            .Where(c => c.IsDeleted == false && c.ConfigKey!.StartsWith("notify_"))
            .ToListAsync();
        foreach (var r in rows)
            if (known.ContainsKey(r.ConfigKey!))
                known[r.ConfigKey!] = r.ConfigValue ?? string.Empty;
        return known;
    }

    /// <summary>保存通知渠道配置（白名单键整体覆盖，未知键拒绝；本操作留痕由控制器侧埋）</summary>
    public async Task SaveAsync(Dictionary<string, string> configs)
    {
        var clean = configs.Where(kv => KnownKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value ?? string.Empty);
        if (clean.Count == 0)
            throw new DomainException("没有可保存的通知渠道配置项", 400);

        foreach (var kv in clean)
        {
            var row = await db.Queryable<SysSiteConfig>().FirstAsync(c => c.ConfigKey == kv.Key && c.IsDeleted == false);
            if (row == null)
            {
                await db.Insertable(new SysSiteConfig
                {
                    Id = snowflake.NextId(),
                    ConfigKey = kv.Key, ConfigValue = kv.Value, Remark = "通知渠道配置（未接线）"
                }).ExecuteCommandAsync();
            }
            else
            {
                row.ConfigValue = kv.Value;
                row.UpdatedAt = DateTime.Now;
                await db.Updateable(row).ExecuteCommandAsync();
            }
        }
    }
}
