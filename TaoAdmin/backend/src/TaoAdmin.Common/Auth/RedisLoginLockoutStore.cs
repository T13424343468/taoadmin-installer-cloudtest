using Microsoft.Extensions.Logging;

namespace TaoAdmin.Common.Auth;

/// <summary>
/// 登录失败锁定账本（Redis 版，第二 round ⑭）：失败计数、锁定状态、剩余锁定时间全部存 Redis 并带过期，
/// 服务重启不清零，撞库防护不再被重启绕过；多实例部署共享同一本账。
/// 键设计：login:lock:fails:{用户名}=窗口内连续失败计数（TTL=锁定时长，窗口到期自动失效）；
/// login:lock:until:{用户名}=锁定标记（TTL=锁定时长，到期自动解锁）。
/// 降级口径（明确）：Redis 不可用时放行登录且不记账（fail-open，保可用性），记 Warning 日志；
/// Redis 恢复后账本继续生效。锁定中的账号在 Redis 恢复前不会被误锁（账本读不到即视为未锁定）。
/// </summary>
public class RedisLoginLockoutStore : ILoginLockoutStore
{
    private const string FailsPrefix = "login:lock:fails:";
    private const string LockPrefix = "login:lock:until:";

    private readonly IRedisLockoutDatabase _db;
    private readonly ILogger<RedisLoginLockoutStore>? _logger;

    public RedisLoginLockoutStore(IRedisLockoutDatabase db, ILogger<RedisLoginLockoutStore>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>是否锁定中；锁定时返回剩余分钟（向上取整，最少 1），未锁定返回 null。</summary>
    public int? GetRemainingLockMinutes(string username, int lockMinutes)
    {
        try
        {
            var ttl = _db.KeyTimeToLive(LockPrefix + username);
            if (ttl is { Ticks: > 0 })
                return Math.Max(1, (int)Math.Ceiling(ttl.Value.TotalMinutes));
            return null;
        }
        catch (Exception ex)
        {
            Warn("读取锁定状态失败，降级放行", username, ex);
            return null;
        }
    }

    /// <summary>记一次失败；达阈值即锁定（锁定起点=now，时长=lockMinutes）。</summary>
    public void RecordFailure(string username, int maxFails, int lockMinutes)
    {
        try
        {
            var window = TimeSpan.FromMinutes(lockMinutes);
            var fails = _db.StringIncrement(FailsPrefix + username);
            if (fails == 1)
                _db.KeyExpire(FailsPrefix + username, window);
            if (fails >= maxFails)
                _db.StringSetWithExpiry(LockPrefix + username, "1", window);
        }
        catch (Exception ex)
        {
            Warn("记录登录失败失败，本次不进锁定账", username, ex);
        }
    }

    /// <summary>清零（登录成功或管理员手工解锁）。</summary>
    public void Clear(string username)
    {
        try
        {
            _db.KeyDelete(FailsPrefix + username);
            _db.KeyDelete(LockPrefix + username);
        }
        catch (Exception ex)
        {
            Warn("清零锁定账本失败", username, ex);
        }
    }

    /// <summary>当前窗口内连续失败次数（无记录=0；锁定中返回 0，与原内存版口径一致）。</summary>
    public int GetFails(string username)
    {
        try
        {
            var ttl = _db.KeyTimeToLive(LockPrefix + username);
            if (ttl is { Ticks: > 0 })
                return 0;
            var raw = _db.StringGet(FailsPrefix + username);
            return int.TryParse(raw, out var fails) ? fails : 0;
        }
        catch (Exception ex)
        {
            Warn("读取失败计数失败，按 0 处理", username, ex);
            return 0;
        }
    }

    /// <summary>测试与运维用：整账清空（按前缀扫描删除）。</summary>
    public void Reset()
    {
        try
        {
            foreach (var key in _db.ScanKeys("login:lock:*"))
                _db.KeyDelete(key);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "清空锁定账本失败（Redis 不可用），保留原账");
        }
    }

    private void Warn(string message, string username, Exception ex)
        => _logger?.LogWarning(ex, "{Message}：{Username}", message, username);
}
