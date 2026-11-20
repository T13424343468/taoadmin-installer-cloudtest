using System.Collections.Concurrent;

namespace TaoAdmin.Common.Auth;

/// <summary>在线会话记录（一条 = 一次登录 = 一个有效令牌）</summary>
public class OnlineSession
{
    /// <summary>会话标识（JWT jti 声明，全局唯一）</summary>
    public string SessionId { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? NickName { get; set; }
    /// <summary>登录来源：客户端 IP</summary>
    public string Ip { get; set; } = string.Empty;
    /// <summary>登录来源：客户端 UserAgent</summary>
    public string UserAgent { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public DateTime LastActiveTime { get; set; }
}

/// <summary>
/// 在线会话仓库：登录登记、活跃续期、强制下线。
/// 当前为进程内实现（单实例语义）；多实例部署时按同一接口换 Redis 实现，
/// 仓储调用方（应用服务/中间件）零改动。
/// 生命周期口径：会话有效期 = 访问令牌有效期（登录起算不滑动），过期自动清出名单；
/// 吊销表保留一个令牌有效期，覆盖被吊销令牌的剩余寿命，防止「下线后复活」。
/// </summary>
public class OnlineSessionStore
{
    private readonly ConcurrentDictionary<string, OnlineSession> _sessions = new();
    private readonly ConcurrentDictionary<string, DateTime> _revoked = new();
    private readonly TimeSpan _tokenLifetime;

    /// <param name="tokenLifetimeMinutes">访问令牌有效期（分钟），决定会话与吊销记录的清理节奏</param>
    public OnlineSessionStore(int tokenLifetimeMinutes = 120)
    {
        _tokenLifetime = TimeSpan.FromMinutes(Math.Max(tokenLifetimeMinutes, 1));
    }

    /// <summary>登录登记（同 jti 重复登录覆盖旧记录，幂等）</summary>
    public void Register(OnlineSession session)
    {
        var now = DateTime.Now;
        session.LoginTime = now;
        session.LastActiveTime = now;
        PruneExpired(now);
        _sessions[session.SessionId] = session;
    }

    /// <summary>活跃续期（更新最后活跃时间，供在线名单展示）</summary>
    public void Touch(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
            s.LastActiveTime = DateTime.Now;
    }

    public OnlineSession? Find(string sessionId)
        => _sessions.TryGetValue(sessionId, out var s) ? s : null;

    /// <summary>强制下线：从名单移除并登记吊销，该令牌即刻失效。返回是否确有此会话</summary>
    public bool Revoke(string sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out _)) return false;
        _revoked[sessionId] = DateTime.Now;
        return true;
    }

    /// <summary>强制下线指定用户的全部会话（改密后全端失效用）。返回下线会话数</summary>
    public int RevokeByUser(long userId)
    {
        var kicked = _sessions.Where(kv => kv.Value.UserId == userId).ToList();
        foreach (var kv in kicked)
        {
            if (_sessions.TryRemove(kv.Key, out _))
                _revoked[kv.Key] = DateTime.Now;
        }
        return kicked.Count;
    }

    /// <summary>令牌是否已被吊销（强制下线/退出后为 true，命中即 401）</summary>
    public bool IsRevoked(string sessionId) => _revoked.ContainsKey(sessionId);

    /// <summary>当前在线名单（按登录时间倒序，先清过期）</summary>
    public List<OnlineSession> List()
    {
        PruneExpired(DateTime.Now);
        return _sessions.Values.OrderByDescending(s => s.LoginTime).ToList();
    }

    /// <summary>清出过期会话与过期吊销记录（随 Register/List 顺带执行，无需后台线程）</summary>
    private void PruneExpired(DateTime now)
    {
        foreach (var kv in _sessions)
        {
            // 会话寿命 = 令牌寿命，登录起算不滑动
            if (now - kv.Value.LoginTime > _tokenLifetime)
                _sessions.TryRemove(kv.Key, out _);
        }
        // 吊销记录保留一个令牌周期：被吊销令牌最晚在吊销时点 + 令牌寿命前失效
        foreach (var kv in _revoked)
        {
            if (now - kv.Value > _tokenLifetime)
                _revoked.TryRemove(kv.Key, out _);
        }
    }
}
