using System.Collections.Concurrent;

namespace TaoAdmin.Common.Auth;

/// <summary>
/// 登录失败锁定账本（进程内，与 OnlineSessionStore 同族；跨服务共享账本属已知遗留，与在线会话同口径）。
/// 键=用户名，窗口内连续失败计数；密码正确即清零；锁定到期自动解锁。
/// </summary>
public class LoginLockoutStore
{
    private sealed record Entry(int Fails, DateTime LockedUntil, DateTime WindowStart);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> _now;

    public LoginLockoutStore(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    /// <summary>是否锁定中；锁定时返回剩余分钟（向上取整），未锁定返回 null。</summary>
    public int? GetRemainingLockMinutes(string username, int lockMinutes)
    {
        if (_entries.TryGetValue(username, out var e) && e.LockedUntil > _now())
            return (int)Math.Ceiling((e.LockedUntil - _now()).TotalMinutes) is var m && m < 1 ? 1 : m;
        return null;
    }

    /// <summary>记一次失败；达阈值即锁定（锁定起点=now，时长=lockMinutes）。</summary>
    public void RecordFailure(string username, int maxFails, int lockMinutes)
    {
        var now = _now();
        _entries.AddOrUpdate(username,
            new Entry(1, now, now),
            (_, e) =>
            {
                // 窗口超过锁定时长视为重新计数（过期自动失效，避免老账冤枉人）
                var fails = (now - e.WindowStart).TotalMinutes > lockMinutes ? 1 : e.Fails + 1;
                var lockedUntil = fails >= maxFails ? now.AddMinutes(lockMinutes) : e.LockedUntil;
                var windowStart = fails >= maxFails ? e.WindowStart : fails == 1 ? now : e.WindowStart;
                return new Entry(fails, lockedUntil, windowStart);
            });
    }

    /// <summary>清零（登录成功或管理员手工解锁）。</summary>
    public void Clear(string username) => _entries.TryRemove(username, out _);

    /// <summary>当前窗口内连续失败次数（无记录=0；供验证码触发阈值判断）。</summary>
    public int GetFails(string username)
        => _entries.TryGetValue(username, out var e)
            && (e.LockedUntil <= _now()) ? e.Fails : 0;

    /// <summary>测试与运维用：整账清空。</summary>
    public void Reset() => _entries.Clear();
}
