namespace TaoAdmin.Common.Auth;

/// <summary>
/// Redis 账本所需的最小操作面：只暴露锁定账本用到的读写与扫描，
/// 生产由 <see cref="RedisLockoutDatabaseAdapter"/> 适配 StackExchange.Redis，测试用内存桩。
/// </summary>
public interface IRedisLockoutDatabase
{
    /// <summary>读字符串值（不存在返回 null）。</summary>
    string? StringGet(string key);

    /// <summary>自增 1 并返回新值（键不存在从 0 开始）。</summary>
    long StringIncrement(string key);

    /// <summary>写字符串并设置过期时间。</summary>
    bool StringSetWithExpiry(string key, string value, TimeSpan expiry);

    /// <summary>为已有键设置过期时间。</summary>
    bool KeyExpire(string key, TimeSpan expiry);

    /// <summary>删除键。</summary>
    bool KeyDelete(string key);

    /// <summary>键剩余存活时间（键不存在返回 null）。</summary>
    TimeSpan? KeyTimeToLive(string key);

    /// <summary>按通配符扫描键名（如 "login:lock:*"）。</summary>
    IEnumerable<string> ScanKeys(string pattern);
}
