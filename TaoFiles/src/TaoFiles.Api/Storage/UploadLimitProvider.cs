using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace TaoFiles.Api.Storage;

/// <summary>上传体积上限提供口（⑬）：全链路显式可配，站点配置为唯一事实源。</summary>
public interface IUploadLimitProvider
{
    /// <summary>当前生效的上传体积上限（字节）。</summary>
    Task<long> GetLimitBytesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 上传体积上限提供（⑭口径同源）：优先拉取权限服务站点配置 uploadMaxSizeMB（60 秒缓存），
/// 站点配置不可达或未配置时回退本地 FileStorage:MaxSizeMB（默认 50MB）——
/// 文件中心独立部署也能跑，但配置以站点配置为准，改站点配置 60 秒内全链路收敛。
/// </summary>
public class UploadLimitProvider(
    IConfiguration config,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    ILogger<UploadLimitProvider>? logger = null) : IUploadLimitProvider
{
    public const string CacheKey = "taofiles:upload-limit-mb";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SiteConfigResponse(SiteConfigData? data);

    private sealed record SiteConfigData(int? uploadMaxSizeMB);

    /// <summary>本地兜底上限（MB）：站点配置不可达时生效。</summary>
    internal long LocalLimitMB => config.GetValue<long>("FileStorage:MaxSizeMB", 50);

    public async Task<long> GetLimitBytesAsync(CancellationToken cancellationToken = default)
    {
        var mb = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await ResolveLimitMBAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return mb * 1024 * 1024;
    }

    /// <summary>解析上限（MB）：站点配置优先，回退本地配置。</summary>
    internal async Task<long> ResolveLimitMBAsync(CancellationToken ct)
    {
        var fromSite = await FetchSiteConfigLimitAsync(ct).ConfigureAwait(false);
        if (fromSite is > 0)
            return fromSite.Value;
        return LocalLimitMB;
    }

    /// <summary>拉取站点配置里的上限；不可达/未配置/非法值返回 null（走本地兜底）。</summary>
    private async Task<long?> FetchSiteConfigLimitAsync(CancellationToken ct)
    {
        var url = config["TaoFiles:SiteConfigUrl"];
        if (string.IsNullOrWhiteSpace(url))
            return null;
        try
        {
            var http = httpFactory.CreateClient("upload-limit");
            http.Timeout = TimeSpan.FromSeconds(3);
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                logger?.LogWarning("站点配置拉取失败 HTTP {Status}，上传上限回退本地配置", (int)resp.StatusCode);
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<SiteConfigResponse>(JsonOptions, ct).ConfigureAwait(false);
            return body?.data?.uploadMaxSizeMB;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning("站点配置拉取异常：{Message}，上传上限回退本地配置", ex.Message);
            return null;
        }
    }
}
