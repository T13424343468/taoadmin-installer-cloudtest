using TaoAdmin.Common.Auth;
using Xunit;

namespace TaoAdmin.Tests.Auth;

/// <summary>
/// Redis 登录锁定账本（内存桩 Redis 测试）：
/// 剩余锁定分钟向上取整且最少 1、首次失败设窗口过期、达阈值写锁定标记、
/// 清零、锁定中计数口径 0、整账扫描清空、Redis 异常逐方法降级（fail-open 不抛出）。
/// </summary>
public class RedisLoginLockoutStoreTests
{
    /// <summary>内存版 Redis 桩：字典 + 过期时间，覆盖账本用到的最小操作面。</summary>
    private sealed class FakeRedis : IRedisLockoutDatabase
    {
        public readonly Dictionary<string, (string? Value, DateTime? Expiry)> Store = new();
        public int CallCount;

        private bool Expired(string key)
            => Store.TryGetValue(key, out var entry) && entry.Expiry is { } exp && exp <= DateTime.UtcNow;

        public string? StringGet(string key)
        {
            CallCount++;
            return Expired(key) ? null : Store.GetValueOrDefault(key).Value;
        }

        public long StringIncrement(string key)
        {
            CallCount++;
            var current = long.TryParse(StringGet(key), out var n) ? n : 0;
            var next = current + 1;
            Store[key] = (next.ToString(), Store.GetValueOrDefault(key).Expiry);
            return next;
        }

        public bool StringSetWithExpiry(string key, string value, TimeSpan expiry)
        {
            CallCount++;
            Store[key] = (value, DateTime.UtcNow.Add(expiry));
            return true;
        }

        public bool KeyExpire(string key, TimeSpan expiry)
        {
            CallCount++;
            if (!Store.ContainsKey(key)) return false;
            Store[key] = (Store[key].Value, DateTime.UtcNow.Add(expiry));
            return true;
        }

        public bool KeyDelete(string key)
        {
            CallCount++;
            return Store.Remove(key);
        }

        public TimeSpan? KeyTimeToLive(string key)
        {
            CallCount++;
            if (!Store.TryGetValue(key, out var entry) || entry.Expiry is not { } exp || exp <= DateTime.UtcNow)
                return null;
            return exp - DateTime.UtcNow;
        }

        public IEnumerable<string> ScanKeys(string pattern)
        {
            CallCount++;
            var prefix = pattern.TrimEnd('*');
            return Store.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }
    }

    /// <summary>全抛异常桩：模拟 Redis 不可用，验证逐方法降级。</summary>
    private sealed class BrokenRedis : IRedisLockoutDatabase
    {
        public string? StringGet(string key) => throw new InvalidOperationException("Redis 不可用");
        public long StringIncrement(string key) => throw new InvalidOperationException("Redis 不可用");
        public bool StringSetWithExpiry(string key, string value, TimeSpan expiry) => throw new InvalidOperationException("Redis 不可用");
        public bool KeyExpire(string key, TimeSpan expiry) => throw new InvalidOperationException("Redis 不可用");
        public bool KeyDelete(string key) => throw new InvalidOperationException("Redis 不可用");
        public TimeSpan? KeyTimeToLive(string key) => throw new InvalidOperationException("Redis 不可用");
        public IEnumerable<string> ScanKeys(string pattern) => throw new InvalidOperationException("Redis 不可用");
    }

    private static RedisLoginLockoutStore Create(FakeRedis db) => new(db);

    [Fact]
    public void GetRemainingLockMinutes_未锁定_ReturnNull()
    {
        var store = Create(new FakeRedis());
        Assert.Null(store.GetRemainingLockMinutes("alice", 15));
    }

    [Fact]
    public void GetRemainingLockMinutes_锁定中剩余90秒_向上取整返回2()
    {
        var db = new FakeRedis();
        db.StringSetWithExpiry("login:lock:until:alice", "1", TimeSpan.FromSeconds(90));
        var store = Create(db);
        Assert.Equal(2, store.GetRemainingLockMinutes("alice", 15));
    }

    [Fact]
    public void GetRemainingLockMinutes_剩余不足1分钟_最少返回1()
    {
        var db = new FakeRedis();
        db.StringSetWithExpiry("login:lock:until:bob", "1", TimeSpan.FromSeconds(5));
        var store = Create(db);
        Assert.Equal(1, store.GetRemainingLockMinutes("bob", 15));
    }

    [Fact]
    public void GetRemainingLockMinutes_Redis异常_降级放行返回Null()
    {
        var store = new RedisLoginLockoutStore(new BrokenRedis());
        Assert.Null(store.GetRemainingLockMinutes("alice", 15));
    }

    [Fact]
    public void RecordFailure_首次失败_计数为1并设置窗口过期()
    {
        var db = new FakeRedis();
        var store = Create(db);
        store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        Assert.Equal(1, store.GetFails("alice"));
        // 首次失败即设置窗口过期：窗口内不累计旧失败
        Assert.NotNull(db.KeyTimeToLive("login:lock:fails:alice"));
    }

    [Fact]
    public void RecordFailure_未达阈值_不锁定()
    {
        var db = new FakeRedis();
        var store = Create(db);
        store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        Assert.Null(store.GetRemainingLockMinutes("alice", 15));
    }

    [Fact]
    public void RecordFailure_达阈值_写入锁定标记带过期()
    {
        var db = new FakeRedis();
        var store = Create(db);
        for (var i = 0; i < 5; i++)
            store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        Assert.NotNull(db.KeyTimeToLive("login:lock:until:alice"));
        Assert.Equal(TimeSpan.FromMinutes(15).TotalSeconds, db.KeyTimeToLive("login:lock:until:alice")!.Value.TotalSeconds, 0);
    }

    [Fact]
    public void RecordFailure_Redis异常_不抛出()
    {
        var store = new RedisLoginLockoutStore(new BrokenRedis());
        store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
    }

    [Fact]
    public void Clear_删除计数与锁定键()
    {
        var db = new FakeRedis();
        var store = Create(db);
        for (var i = 0; i < 5; i++)
            store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        store.Clear("alice");
        Assert.Equal(0, store.GetFails("alice"));
        Assert.Null(store.GetRemainingLockMinutes("alice", 15));
    }

    [Fact]
    public void Clear_Redis异常_不抛出()
    {
        var store = new RedisLoginLockoutStore(new BrokenRedis());
        store.Clear("alice");
    }

    [Fact]
    public void GetFails_无记录_Return0()
    {
        var store = Create(new FakeRedis());
        Assert.Equal(0, store.GetFails("nobody"));
    }

    [Fact]
    public void GetFails_锁定中_Return0_与原内存版口径一致()
    {
        var db = new FakeRedis();
        var store = Create(db);
        for (var i = 0; i < 5; i++)
            store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        Assert.Equal(0, store.GetFails("alice"));
    }

    [Fact]
    public void GetFails_Redis异常_Return0()
    {
        var store = new RedisLoginLockoutStore(new BrokenRedis());
        Assert.Equal(0, store.GetFails("alice"));
    }

    [Fact]
    public void GetFails_计数值非数字_Return0()
    {
        var db = new FakeRedis();
        db.Store["login:lock:fails:alice"] = ("not-a-number", null);
        var store = Create(db);
        Assert.Equal(0, store.GetFails("alice"));
    }

    [Fact]
    public void Reset_按前缀扫描删除全部账目()
    {
        var db = new FakeRedis();
        var store = Create(db);
        for (var i = 0; i < 5; i++)
            store.RecordFailure("alice", maxFails: 5, lockMinutes: 15);
        for (var i = 0; i < 3; i++)
            store.RecordFailure("bob", maxFails: 5, lockMinutes: 15);
        store.Reset();
        Assert.Empty(db.Store);
    }

    [Fact]
    public void Reset_Redis异常_不抛出()
    {
        var store = new RedisLoginLockoutStore(new BrokenRedis());
        store.Reset();
    }
}
