using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace TaoAdmin.Common.Cache;

/// <summary>
/// Redis 缓存封装：基于 IDatabase，值统一使用 JSON 序列化。
/// 提供 Get / Set / Del / HGet / HSet / Exists / Expire 等常用操作。
/// </summary>
public class RedisHelper
{
    private readonly IDatabase _db;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 中文等非 ASCII 字符不转义，便于排查
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public RedisHelper(IConnectionMultiplexer multiplexer)
    {
        _db = multiplexer.GetDatabase();
    }

    #region String

    /// <summary>读取缓存并反序列化为对象，不存在返回 default</summary>
    public T? Get<T>(string key)
    {
        var value = _db.StringGet(key);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value!.ToString(), JsonOptions);
    }

    /// <summary>读取原始字符串</summary>
    public string? GetString(string key)
    {
        var value = _db.StringGet(key);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    /// <summary>写入缓存（对象自动 JSON 序列化），expiry 为空表示永不过期</summary>
    public bool Set<T>(string key, T value, TimeSpan? expiry = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        // StackExchange.Redis 3.x：过期改用 Expiration 类型，Default 即不过期（语义与旧 TimeSpan? null 一致）
        return _db.StringSet(key, json, expiry.HasValue ? expiry.Value : Expiration.Default);
    }

    /// <summary>写入原始字符串</summary>
    public bool SetString(string key, string value, TimeSpan? expiry = null)
        => _db.StringSet(key, value, expiry.HasValue ? expiry.Value : Expiration.Default);

    #endregion

    #region Key 操作

    /// <summary>删除一个或多个缓存键</summary>
    public long Del(params string[] keys)
    {
        if (keys == null || keys.Length == 0) return 0;
        var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
        return _db.KeyDelete(redisKeys);
    }

    /// <summary>判断缓存键是否存在</summary>
    public bool Exists(string key) => _db.KeyExists(key);

    /// <summary>为已有缓存键设置过期时间</summary>
    public bool Expire(string key, TimeSpan expiry) => _db.KeyExpire(key, expiry);

    #endregion

    #region Hash

    /// <summary>读取 Hash 字段并反序列化为对象</summary>
    public T? HGet<T>(string key, string field)
    {
        var value = _db.HashGet(key, field);
        return value.IsNullOrEmpty ? default : JsonSerializer.Deserialize<T>(value!.ToString(), JsonOptions);
    }

    /// <summary>写入 Hash 字段（对象自动 JSON 序列化）</summary>
    public bool HSet<T>(string key, string field, T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return _db.HashSet(key, field, json);
    }

    /// <summary>删除 Hash 字段</summary>
    public bool HDel(string key, string field) => _db.HashDelete(key, field);

    /// <summary>读取整个 Hash（字段 -> JSON 字符串）</summary>
    public Dictionary<string, string> HGetAll(string key)
    {
        var entries = _db.HashGetAll(key);
        return entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString());
    }

    #endregion
}
