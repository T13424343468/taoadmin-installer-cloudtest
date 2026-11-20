using System.Collections.Concurrent;
using System.Text.Json;
using TaoAdmin.Common.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TaoAdmin.Common.Idempotency;

/// <summary>
/// 幂等标记：标注在需要防重复提交的接口上。
/// 客户端请求头携带 Idempotency-Key（任意唯一值，如 UUID），同一 Key 在 TTL 内重复请求
/// 直接返回第一次的结果，不重复执行业务（防连点/重试导致重复下单、重复提交）。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class IdempotentAttribute : Attribute
{
    /// <summary>幂等保留时长（秒），默认 600 秒</summary>
    public int TtlSeconds { get; set; } = 600;
}

/// <summary>
/// 幂等过滤器（全局注册，仅对标注 [Idempotent] 的接口生效）。
/// 幂等键 = 租户 + 用户 + 请求路径 + Idempotency-Key。
/// 【本地】默认内存实现；【线上】多实例部署建议把 IdempotencyStore 换成 Redis 实现（接口已抽象）。
/// </summary>
public class IdempotencyFilter : IAsyncActionFilter
{
    private readonly IdempotencyStore _store;

    public IdempotencyFilter(IdempotencyStore store) => _store = store;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var marker = context.ActionDescriptor.EndpointMetadata.OfType<IdempotentAttribute>().FirstOrDefault();
        if (marker == null)
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key))
        {
            context.Result = new JsonResult(new ApiResult<object> { Code = 400, Success = false, Message = "该接口要求携带 Idempotency-Key 请求头防重复提交" });
            return;
        }

        var cacheKey = $"{http.User.FindFirst("tenant_id")?.Value ?? "0"}:{http.User.FindFirst("UserId")?.Value ?? "anon"}:{http.Request.Path}:{key}";
        var cached = _store.TryGet(cacheKey);
        if (cached != null)
        {
            // 命中幂等缓存：直接返回首次结果，不重复执行
            var result = JsonSerializer.Deserialize<ApiResult<object>>(cached);
            http.Response.Headers["X-Idempotent-Replayed"] = "true";
            context.Result = new JsonResult(result);
            return;
        }

        var executed = await next();
        if (executed.Exception == null && executed.Result != null)
        {
            var payload = executed.Result switch
            {
                ObjectResult o => o.Value,
                ContentResult c => c.Content,
                _ => (object?)null
            };
            if (payload != null)
            {
                var body = JsonSerializer.Serialize(payload);
                _store.Save(cacheKey, body, TimeSpan.FromSeconds(marker.TtlSeconds));
            }
        }
    }
}

/// <summary>幂等缓存存取（内存实现；多实例部署可替换为 Redis 实现）</summary>
public class IdempotencyStore
{
    private class Entry
    {
        public string Body = "";
        public DateTime ExpireAt;
    }

    private readonly ConcurrentDictionary<string, Entry> _cache = new();

    public string? TryGet(string key)
    {
        if (!_cache.TryGetValue(key, out var entry)) return null;
        if (DateTime.Now > entry.ExpireAt)
        {
            _cache.TryRemove(key, out _);
            return null;
        }
        return entry.Body;
    }

    public void Save(string key, string body, TimeSpan ttl)
    {
        _cache[key] = new Entry { Body = body, ExpireAt = DateTime.Now.Add(ttl) };
    }
}
