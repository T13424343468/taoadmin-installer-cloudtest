using StackExchange.Redis;

namespace TaoAdmin.Common.Auth;

/// <summary>StackExchange.Redis 适配：把 IDatabase/IServer 收敛成账本所需的最小操作面。</summary>
public class RedisLockoutDatabaseAdapter : IRedisLockoutDatabase
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _multiplexer;

    public RedisLockoutDatabaseAdapter(IConnectionMultiplexer multiplexer)
    {
        _multiplexer = multiplexer;
        _db = multiplexer.GetDatabase();
    }

    public string? StringGet(string key)
    {
        var value = _db.StringGet(key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    public long StringIncrement(string key) => _db.StringIncrement(key);

    public bool StringSetWithExpiry(string key, string value, TimeSpan expiry)
        => _db.StringSet(key, value, expiry);

    public bool KeyExpire(string key, TimeSpan expiry) => _db.KeyExpire(key, expiry);

    public bool KeyDelete(string key) => _db.KeyDelete(key);

    public TimeSpan? KeyTimeToLive(string key) => _db.KeyTimeToLive(key);

    public IEnumerable<string> ScanKeys(string pattern)
    {
        var endPoint = _multiplexer.GetEndPoints().First();
        var server = _multiplexer.GetServer(endPoint);
        return server.Keys(_db.Database, pattern, 100).Select(k => k.ToString());
    }
}
