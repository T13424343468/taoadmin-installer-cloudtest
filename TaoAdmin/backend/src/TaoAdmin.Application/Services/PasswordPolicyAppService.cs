using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Utilities;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>密码策略（管理员可配置）：最小长度、至少几类字符、多少天强制改密（0=不强制）。</summary>
public record PasswordPolicy(int MinLength, int MinKinds, int ForceChangeDays);

/// <summary>密码策略读取口（供改密/建人/登录等调用方注入，测试可替换桩）。</summary>
public interface IPasswordPolicyProvider
{
    /// <summary>读取当前生效策略（未配置的键返回内置默认值）。</summary>
    Task<PasswordPolicy> GetAsync();

    /// <summary>当前策略校验口令：通过返回 null，否则返回明确文案（默认实现=取策略后走纯函数，测试桩可只实现 GetAsync）。</summary>
    async Task<string?> CheckAsync(string? password)
    {
        var policy = await GetAsync();
        return ValidateHelper.CheckPassword(password, policy.MinLength, policy.MinKinds);
    }
}

/// <summary>强制改密判定（纯函数，登录/取当前用户共用一处口径）。</summary>
public static class PasswordPolicyEx
{
    /// <summary>
    /// 是否需要强制改密：forceChangeDays&gt;0 且改密基准（最后改密时间，缺省回退建号时间）距今超过 N 天。
    /// 存量兼容：PasswordUpdatedAt 为空的用户按 CreatedAt 兜底，避免策略上线瞬间全员误报。
    /// </summary>
    public static bool IsExpired(PasswordPolicy policy, DateTime? passwordUpdatedAt, DateTime createdAt, DateTime now)
    {
        if (policy.ForceChangeDays <= 0) return false;
        var baseline = passwordUpdatedAt ?? createdAt;
        return baseline.AddDays(policy.ForceChangeDays) < now;
    }
}

/// <summary>
/// 密码策略应用服务：键值存 sys_site_config（pwdMinLength/pwdMinKinds/pwdForceChangeDays），
/// 复用站点参数的缓存-aside 模式——保存即清缓存，下一次读取落库，改完即生效不重启。
/// 内置默认值=既有校验工具口径（8 位、3 类、不强制改密），策略只约束新设密码，存量口令不动。
/// </summary>
public class PasswordPolicyAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake, IMemoryCache cache) : IPasswordPolicyProvider
{
    /// <summary>内置默认值（迁移既有内置规则）：最小 8 位、至少 3 类字符、不强制改密</summary>
    public const int DefaultMinLength = 8;
    public const int DefaultMinKinds = 3;
    public const int DefaultForceChangeDays = 0;

    /// <summary>策略缓存键（独立于站点门面缓存，保存后整键清除）</summary>
    public const string CacheKey = "sys:pwd-policy:v1";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>读取当前策略（命中缓存不落库；未配置的键返回默认值）</summary>
    public async Task<PasswordPolicy> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out PasswordPolicy? cached) && cached != null)
            return cached;

        var rows = await db.Queryable<SysSiteConfig>()
            .Where(c => c.IsDeleted == false && c.ConfigKey!.StartsWith("pwd"))
            .ToListAsync();
        int IntOf(string key, int def)
            => rows.FirstOrDefault(r => r.ConfigKey == key)?.ConfigValue is { Length: > 0 } v
                && int.TryParse(v, out var n) ? n : def;

        var policy = new PasswordPolicy(
            IntOf(PolicyKeys.MinLength, DefaultMinLength),
            IntOf(PolicyKeys.MinKinds, DefaultMinKinds),
            IntOf(PolicyKeys.ForceChangeDays, DefaultForceChangeDays));
        cache.Set(CacheKey, policy, CacheTtl);
        return policy;
    }

    /// <summary>保存策略（整体覆盖，保存即清缓存立即生效）；边界值当场校验，防管理员手滑锁死全站。</summary>
    public async Task SaveAsync(PasswordPolicy req)
    {
        if (req.MinLength is < 6 or > 64)
            throw new DomainException("密码最小长度须在 6~64 位之间", 400);
        if (req.MinKinds is < 1 or > 4)
            throw new DomainException("字符类数须在 1~4 之间", 400);
        if (req.ForceChangeDays is < 0 or > 3650)
            throw new DomainException("强制改密天数须在 0~3650 之间（0 表示不强制）", 400);

        await UpsertAsync(PolicyKeys.MinLength, req.MinLength.ToString(), "密码策略：最小长度");
        await UpsertAsync(PolicyKeys.MinKinds, req.MinKinds.ToString(), "密码策略：至少字符类数");
        await UpsertAsync(PolicyKeys.ForceChangeDays, req.ForceChangeDays.ToString(), "密码策略：强制改密天数");

        cache.Remove(CacheKey);
    }

    /// <summary>当前策略校验口令：通过返回 null，否则返回明确文案（与改密/建人共用一处）。</summary>
    public async Task<string?> CheckAsync(string? password)
    {
        var policy = await GetAsync();
        return ValidateHelper.CheckPassword(password, policy.MinLength, policy.MinKinds);
    }

    private async Task UpsertAsync(string key, string value, string remark)
    {
        var row = await db.Queryable<SysSiteConfig>().FirstAsync(c => c.ConfigKey == key && c.IsDeleted == false);
        if (row == null)
        {
            await db.Insertable(new SysSiteConfig
            {
                Id = snowflake.NextId(),
                ConfigKey = key, ConfigValue = value, Remark = remark
            }).ExecuteCommandAsync();
        }
        else
        {
            row.ConfigValue = value;
            row.UpdatedAt = DateTime.Now;
            await db.Updateable(row).ExecuteCommandAsync();
        }
    }
}

/// <summary>策略键（sys_site_config 的 ConfigKey 取值，集中一处防拼错）</summary>
public static class PolicyKeys
{
    public const string MinLength = "pwdMinLength";
    public const string MinKinds = "pwdMinKinds";
    public const string ForceChangeDays = "pwdForceChangeDays";
}
