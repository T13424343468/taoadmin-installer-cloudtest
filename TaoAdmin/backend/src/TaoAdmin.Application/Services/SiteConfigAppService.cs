using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>站点参数保存请求（管理员设置页提交）</summary>
public class SiteConfigSaveRequest
{
    /// <summary>站点名称</summary>
    public string siteName { get; set; } = "";
    /// <summary>Logo 图片地址（URL 或站内路径）</summary>
    public string? siteLogo { get; set; }
    /// <summary>页脚文案</summary>
    public string? siteFooter { get; set; }
    /// <summary>上传体积上限（MB，1~2048；前端选文件即校验，文件中心后端同口径拦截）</summary>
    public int uploadMaxSizeMB { get; set; } = 50;
}

/// <summary>站点参数（前台渲染用，未配置的键返回默认值）</summary>
public class SiteConfigDto
{
    /// <summary>站点名称</summary>
    public string siteName { get; set; } = "";
    /// <summary>Logo 图片地址（未配置为空串，前台用默认图标）</summary>
    public string siteLogo { get; set; } = "";
    /// <summary>页脚文案</summary>
    public string siteFooter { get; set; } = "";
    /// <summary>上传体积上限（MB；前端选文件即校验，文件中心后端同口径拦截）</summary>
    public int uploadMaxSizeMB { get; set; } = 50;
}

/// <summary>
/// 站点参数应用服务：站点名称/Logo/页脚的门面参数维护与读取。
/// 单行键值对结构，未配置的键返回内置默认值。
/// 缓存策略：读走内存缓存（cache-aside，60 秒滑动兜底），保存成功即清缓存，
/// 下一次读取立即命中新值——改完即生效，不需要重启或等待；多实例部署时其他实例
/// 最迟在缓存 TTL 内自动收敛（如需秒级广播可再接 Redis 发布订阅，当前不留口子）。
/// </summary>
public class SiteConfigAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake, IMemoryCache cache)
{
    /// <summary>常用参数键：站点名称 / Logo / 页脚</summary>
    public const string KeySiteName = "siteName";
    public const string KeySiteLogo = "siteLogo";
    public const string KeySiteFooter = "siteFooter";
    /// <summary>上传体积上限键（MB）</summary>
    public const string KeyUploadMaxSizeMB = "uploadMaxSizeMB";

    /// <summary>内置默认值：站点名称（与现有界面文案一致），Logo/页脚为空表示未配置；上传体积默认 50MB</summary>
    public const string DefaultSiteName = "TaoAdmin";
    public const string DefaultSiteFooter = "TaoAdmin";
    public const string DefaultSiteLogo = "";
    public const int DefaultUploadMaxSizeMB = 50;

    /// <summary>缓存键：整个站点参数 DTO 存一份，保存后整键清除</summary>
    public const string CacheKey = "sys:site-config:v1";

    /// <summary>缓存 TTL：单实例保存即清缓存、立即生效；多实例靠 TTL 收敛，60 秒足够短</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>读取站点参数（登录页与主布局渲染用；命中缓存不落库，未配置返回默认值）</summary>
    public async Task<SiteConfigDto> GetAsync()
    {
        if (cache.TryGetValue(CacheKey, out SiteConfigDto? cached) && cached != null)
            return cached;

        var rows = await db.Queryable<SysSiteConfig>().Where(c => c.IsDeleted == false).ToListAsync();
        string ValueOf(string key, string def)
            => rows.FirstOrDefault(r => r.ConfigKey == key)?.ConfigValue is { Length: > 0 } v ? v : def;
        var dto = new SiteConfigDto
        {
            siteName = ValueOf(KeySiteName, DefaultSiteName),
            siteLogo = ValueOf(KeySiteLogo, DefaultSiteLogo),
            siteFooter = ValueOf(KeySiteFooter, DefaultSiteFooter),
            uploadMaxSizeMB = rows.FirstOrDefault(r => r.ConfigKey == KeyUploadMaxSizeMB)?.ConfigValue is { Length: > 0 } size
                && int.TryParse(size, out var mb) && mb > 0 ? mb : DefaultUploadMaxSizeMB
        };
        cache.Set(CacheKey, dto, CacheTtl);
        return dto;
    }

    /// <summary>保存站点参数（整体覆盖式：请求中的键全部落库，空白站点名回退校验，保存后立即清缓存）</summary>
    public async Task SaveAsync(SiteConfigSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.siteName))
            throw new TaoAdmin.Domain.DomainException("站点名称不能为空", 400);
        if (req.uploadMaxSizeMB is < 1 or > 2048)
            throw new TaoAdmin.Domain.DomainException("上传体积上限须在 1~2048MB 之间", 400);

        await UpsertAsync(KeySiteName, req.siteName.Trim(), "站点名称");
        await UpsertAsync(KeySiteLogo, req.siteLogo?.Trim() ?? "", "站点Logo地址");
        await UpsertAsync(KeySiteFooter, req.siteFooter?.Trim() ?? "", "站点页脚文案");
        await UpsertAsync(KeyUploadMaxSizeMB, req.uploadMaxSizeMB.ToString(), "上传体积上限（MB）");

        // 保存即清缓存：下一次读取直接落库，前台立即看到新门面
        cache.Remove(CacheKey);
    }

    /// <summary>单键写入：存在则更新，不存在则插入（幂等）</summary>
    private async Task UpsertAsync(string key, string value, string remark)
    {
        var row = await db.Queryable<SysSiteConfig>().FirstAsync(c => c.ConfigKey == key && c.IsDeleted == false);
        if (row == null)
        {
            await db.Insertable(new SysSiteConfig
            {
                Id = snowflake.NextId(), ConfigKey = key, ConfigValue = value, Remark = remark
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
