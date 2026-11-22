using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Common.Snowflake;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;
using static TaoAdmin.Application.Services.LoginSecurityKeys;

namespace TaoAdmin.Application.Services;

/// <summary>登录安全设置：失败锁定阈值/时长、验证码开关与触发阈值、日志保留天数、同账号互踢开关。</summary>
public record LoginSecuritySettings(
    int LoginMaxFails, int LoginLockMinutes, bool LoginCaptchaRequired, int CaptchaAfterFails,
    int LogRetentionDays, bool KickOnDuplicateLogin);

/// <summary>登录安全策略读取口（供登录流程注入，测试可替换桩）。</summary>
public interface ILoginSecurityProvider
{
    /// <summary>读取当前生效设置（未配置的键返回内置默认值）。</summary>
    Task<LoginSecuritySettings> GetAsync();
}

/// <summary>
/// 登录安全设置应用服务：键值存 sys_site_config（loginMaxFails/loginLockMinutes/loginCaptchaRequired/
/// captchaAfterFails/logRetentionDays），复用密码策略的缓存-aside 模式——保存即清缓存，改完即生效不重启。
/// 内置默认值：连错 5 次锁 15 分钟、每次都要验证码、日志保留 90 天（0=永久）。
/// </summary>
public class LoginSecurityAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake, IMemoryCache cache) : ILoginSecurityProvider
{
    public const int DefaultMaxFails = 5;
    public const int DefaultLockMinutes = 15;
    public const bool DefaultCaptchaRequired = true;
    public const int DefaultCaptchaAfterFails = 0;
    public const int DefaultLogRetentionDays = 90;
    /// <summary>同账号互踢默认关闭：保持现状，多端同时在线不受影响（老板 2026-11-22 对账项④口径）</summary>
    public const bool DefaultKickOnDuplicateLogin = false;

    public const string CacheKey = "sys:login-security:v1";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>读取当前设置（命中缓存不落库；未配置的键返回默认值）</summary>
    public async Task<LoginSecuritySettings> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out LoginSecuritySettings? cached) && cached != null)
            return cached;

        var rows = await db.Queryable<SysSiteConfig>()
            .Where(c => c.IsDeleted == false && (
                c.ConfigKey == LoginSecurityKeys.MaxFails || c.ConfigKey == LoginSecurityKeys.LockMinutes ||
                c.ConfigKey == LoginSecurityKeys.CaptchaRequired || c.ConfigKey == LoginSecurityKeys.CaptchaAfterFails ||
                c.ConfigKey == LoginSecurityKeys.LogRetentionDays || c.ConfigKey == LoginSecurityKeys.KickOnDuplicateLogin))
            .ToListAsync();
        int IntOf(string key, int def)
            => rows.FirstOrDefault(r => r.ConfigKey == key)?.ConfigValue is { Length: > 0 } v
                && int.TryParse(v, out var n) ? n : def;

        var required = rows.FirstOrDefault(r => r.ConfigKey == LoginSecurityKeys.CaptchaRequired)?.ConfigValue;
        var kick = rows.FirstOrDefault(r => r.ConfigKey == LoginSecurityKeys.KickOnDuplicateLogin)?.ConfigValue;
        var settings = new LoginSecuritySettings(
            Math.Max(IntOf(LoginSecurityKeys.MaxFails, DefaultMaxFails), 1),
            Math.Max(IntOf(LoginSecurityKeys.LockMinutes, DefaultLockMinutes), 1),
            required == null ? DefaultCaptchaRequired : required != "0",
            IntOf(LoginSecurityKeys.CaptchaAfterFails, DefaultCaptchaAfterFails),
            Math.Max(IntOf(LoginSecurityKeys.LogRetentionDays, DefaultLogRetentionDays), 0),
            kick == null ? DefaultKickOnDuplicateLogin : kick != "0");
        cache.Set(CacheKey, settings, CacheTtl);
        return settings;
    }

    /// <summary>保存设置（整体覆盖，保存即清缓存立即生效）；边界值当场校验，防管理员手滑锁死全站。</summary>
    public async Task SaveAsync(LoginSecuritySettings req)
    {
        if (req.LoginMaxFails is < 1 or > 99)
            throw new DomainException("连续失败次数须在 1~99 次之间", 400);
        if (req.LoginLockMinutes is < 1 or > 1440)
            throw new DomainException("锁定时长须在 1~1440 分钟之间", 400);
        if (req.CaptchaAfterFails is < 0 or > 99)
            throw new DomainException("验证码触发阈值须在 0~99 之间（0 表示每次都要）", 400);
        if (req.LogRetentionDays is < 0 or > 3650)
            throw new DomainException("日志保留天数须在 0~3650 之间（0 表示永久保留）", 400);

        await UpsertAsync(LoginSecurityKeys.MaxFails, req.LoginMaxFails.ToString(), "登录安全：连续失败锁定次数");
        await UpsertAsync(LoginSecurityKeys.LockMinutes, req.LoginLockMinutes.ToString(), "登录安全：锁定时长（分钟）");
        await UpsertAsync(LoginSecurityKeys.CaptchaRequired, req.LoginCaptchaRequired ? "1" : "0", "登录安全：验证码开关");
        await UpsertAsync(LoginSecurityKeys.CaptchaAfterFails, req.CaptchaAfterFails.ToString(), "登录安全：验证码触发阈值（0=每次都要）");
        await UpsertAsync(LoginSecurityKeys.LogRetentionDays, req.LogRetentionDays.ToString(), "日志保留天数（0=永久保留）");
        await UpsertAsync(LoginSecurityKeys.KickOnDuplicateLogin, req.KickOnDuplicateLogin ? "1" : "0", "登录安全：同账号互踢开关（开启后新登录踢掉旧会话）");

        cache.Remove(CacheKey);
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

/// <summary>登录安全键（sys_site_config 的 ConfigKey 取值，集中一处防拼错）</summary>
public static class LoginSecurityKeys
{
    public const string MaxFails = "loginMaxFails";
    public const string LockMinutes = "loginLockMinutes";
    public const string CaptchaRequired = "loginCaptchaRequired";
    public const string CaptchaAfterFails = "captchaAfterFails";
    public const string LogRetentionDays = "logRetentionDays";
    public const string KickOnDuplicateLogin = "kickOnDuplicateLogin";
}

/// <summary>短信发送口子（只埋不接线）：实现注册 NullSmsSender 占位，接真实通道时换实现。</summary>
public interface ISmsSender
{
    /// <summary>发送短信（占位实现返回成功，不做真实请求）。</summary>
    Task<(bool Ok, string Message)> SendAsync(string phone, string content);
}

/// <summary>邮件发送口子（只埋不接线）：实现注册 NullEmailSender 占位，接真实通道时换实现。</summary>
public interface IEmailSender
{
    /// <summary>发送邮件（占位实现返回成功，不做真实请求）。</summary>
    Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body);
}

/// <summary>短信占位实现：通道未接线，恒返回成功占位（不发真实请求、不留队列）。</summary>
public class NullSmsSender : ISmsSender
{
    public const string PlaceholderMessage = "短信通道未接线，仅供配置留存";
    public Task<(bool Ok, string Message)> SendAsync(string phone, string content)
        => Task.FromResult((true, PlaceholderMessage));
}

/// <summary>邮件占位实现：通道未接线，恒返回成功占位（不发真实请求、不留队列）。</summary>
public class NullEmailSender : IEmailSender
{
    public const string PlaceholderMessage = "邮件通道未接线，仅供配置留存";
    public Task<(bool Ok, string Message)> SendAsync(string to, string subject, string body)
        => Task.FromResult((true, PlaceholderMessage));
}
