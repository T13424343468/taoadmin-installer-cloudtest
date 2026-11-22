using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaoAdmin.Application.Services;

namespace TaoAdmin.Infrastructure.Persistence;

/// <summary>
/// 民政部国家地名信息库在线数据源（老板 2026-11-22 令⑪地址库：更新时自动抓取民政部官方发布渠道的最新数据）。
/// 官方渠道：民政一体化政务服务平台「行政区划查询」接口（zwfw.mca.gov.cn/gateway/webglbiz），
/// 每年 1 月发布截至上年末的全国省/地/县/乡四级区划代码，无需登录即可查询。
/// 抓取策略：先取省级清单，再逐省拉取完整子树（单请求含全部下级），任一省失败整体失败并留明确错误；
/// 官方站点结构变化导致解析失败时抛出含阶段与原因的异常，本地数据原样不动。
/// </summary>
public partial class McaRegionDataSource : IRegionDataSource
{
    /// <summary>HttpClient 命名（DI 侧 AddHttpClient 配置超时）</summary>
    public const string ClientName = "McaRegion";

    /// <summary>子级查询接口（parentCode 需 12 位区划码）</summary>
    private const string ChildrenEndpoint = "interface/service/queryXzqhChildren";

    /// <summary>子树查询接口（searchType=2 按代码查含子树；返回行内嵌完整子树）</summary>
    private const string SubtreeEndpoint = "interface/service/queryXzqhList";

    /// <summary>版本说明页（含「数据截止日期为XXXX年XX月XX日」文案，用于数据版本展示）</summary>
    private const string VersionPageUrl = "https://zwfw.mca.gov.cn/appsv2/xzqhcx/index.html";

    private static readonly string GatewayBase = "https://zwfw.mca.gov.cn/gateway/webglbiz/";

    private readonly HttpClient _http;
    private readonly ILogger<McaRegionDataSource> _logger;

    public McaRegionDataSource(HttpClient http, ILogger<McaRegionDataSource> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<RegionSnapshot> FetchLatestAsync(Action<RegionFetchProgress> onProgress, CancellationToken ct)
    {
        // 阶段一：连接数据源——取省级清单
        onProgress(new RegionFetchProgress { Stage = "连接数据源", FetchedCount = 0, Percent = 2 });
        var provinces = await PostFormAsync(ChildrenEndpoint, new Dictionary<string, string> { ["parentCode"] = "" }, ct);
        var provinceRows = ParseRowsOrList(provinces);
        if (provinceRows == null || provinceRows.Count == 0)
            throw new InvalidOperationException("连接数据源失败：民政部平台省级清单为空或返回结构已变化，无法继续更新");

        // 过滤无有效代码的占位条目（如「台湾省」代码显示为「资料暂缺」）
        var targets = provinceRows
            .Where(p => !string.IsNullOrWhiteSpace(p.displaycode) && p.displaycode.All(char.IsDigit))
            .ToList();
        if (targets.Count is < 20 or > 40)
            throw new InvalidOperationException($"连接数据源失败：省级清单数量 {targets.Count} 异常，疑似接口结构变化");

        // 阶段二：抓取数据——逐省拉取完整子树
        var nodes = new List<RegionNodeDto>();
        long fetched = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (code, name) = (targets[i].displaycode!, targets[i].areaname);
            RegionNodeDto? tree;
            try
            {
                tree = await FetchProvinceSubtreeAsync(code, name, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException($"抓取数据失败：{name}（{code}）子树获取未成功——{ex.Message}", ex);
            }
            if (tree == null)
                throw new InvalidOperationException($"抓取数据失败：{name}（{code}）返回中未找到该省根节点，页面结构可能已变化");

            nodes.Add(tree);
            fetched += CountNodes(tree);
            onProgress(new RegionFetchProgress
            {
                Stage = "抓取数据",
                FetchedCount = fetched,
                Percent = 2 + (int)((i + 1) * 68.0 / targets.Count)
            });
        }

        // 数据版本：官方说明页的「数据截止日期」文案；取不到时退化为抓取日期
        var version = await TryFetchVersionAsync(ct) ?? $"民政部国家地名信息库·抓取于{DateTime.Now:yyyy年MM月dd日}";
        return new RegionSnapshot
        {
            Version = version,
            Source = "民政部国家地名信息库在线抓取（zwfw.mca.gov.cn 行政区划查询）",
            Nodes = nodes
        };
    }

    /// <summary>拉取单省完整子树（接口按行去重引用，取与省码相同的根行，其 children 即完整子树）</summary>
    private async Task<RegionNodeDto?> FetchProvinceSubtreeAsync(string code, string name, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var resp = await PostFormAsync(SubtreeEndpoint, new Dictionary<string, string>
                {
                    ["searchStr"] = code,
                    ["searchType"] = "2",
                    ["pageNum"] = "1",
                    ["pageSize"] = "500"
                }, ct);
                var rows = ParseRowsOrList(resp);
                if (rows == null || rows.Count == 0)
                {
                    _logger.LogWarning("区划抓取：{Name}（{Code}）子树返回为空（第 {Attempt} 次）", name, code, attempt);
                    continue;
                }
                var root = rows.FirstOrDefault(r => r.displaycode == code);
                if (root == null) continue;
                return ToNode(root);
            }
            catch (Exception ex) when (attempt < 2)
            {
                _logger.LogWarning(ex, "区划抓取：{Name}（{Code}）第 {Attempt} 次请求失败，重试", name, code, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        return null;
    }

    private static RegionNodeDto ToNode(ApiRow row) => new()
    {
        Code = row.displaycode ?? "",
        Name = row.areaname ?? "",
        Level = DeriveLevel(row.displaycode ?? ""),
        Children = (row.children ?? []).Select(ToNode).ToList()
    };

    /// <summary>统计子树节点总数（含根）</summary>
    private static long CountNodes(RegionNodeDto root)
        => 1 + root.Children.Sum(CountNodes);

    /// <summary>按国标码长推导层级：6 位尾 0000=省、尾 00=地级市、其余 6 位=区县、9 位=街道（乡镇）</summary>
    public static int DeriveLevel(string code)
    {
        if (code.Length == 9) return 4;
        if (code.EndsWith("0000", StringComparison.Ordinal)) return 1;
        if (code.EndsWith("00", StringComparison.Ordinal)) return 2;
        return 3;
    }

    /// <summary>读取官方说明页的数据截止日期作为版本号；失败返回 null（调用方退化处理）</summary>
    private async Task<string?> TryFetchVersionAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(VersionPageUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var html = await resp.Content.ReadAsStringAsync(ct);
            var m = VersionRegex().Match(html);
            return m.Success ? $"民政部国家地名信息库·数据截止{m.Groups[1].Value}" : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "区划抓取：数据版本说明页读取失败，版本号退化为抓取日期");
            return null;
        }
    }

    [GeneratedRegex(@"数据截止日期为([0-9]{4}年[0-9]{1,2}月[0-9]{1,2}日)")]
    private static partial Regex VersionRegex();

    private async Task<System.Text.Json.JsonElement?> PostFormAsync(string endpoint, Dictionary<string, string> fields, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(fields)
        {
            ["nvc"] = "1",
            ["checkCode"] = ""
        });
        using var resp = await _http.PostAsync(GatewayBase + endpoint, content, ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"接口 {endpoint} 返回 HTTP {(int)resp.StatusCode}");
        var envelope = await resp.Content.ReadFromJsonAsync<ApiEnvelope>(cancellationToken: ct);
        var err = envelope?.errors?.FirstOrDefault();
        if (err != null)
            throw new InvalidOperationException($"接口 {endpoint} 返回错误 {err.errorCode}：{err.msg}");
        return envelope?.data;
    }

    /// <summary>
    /// 兼容两种返回结构：子级查询 data.data 为行数组，子树查询 data.data 为 {total, rows} 对象。
    /// 行数组中可能混有 JSON $ref 引用串（服务端按引用去重），仅保留对象行。
    /// </summary>
    private static List<ApiRow>? ParseRowsOrList(System.Text.Json.JsonElement? data)
    {
        if (data == null || data.Value.ValueKind != System.Text.Json.JsonValueKind.Object)
            return null;
        var inner = data.Value.GetProperty("data");
        if (inner.ValueKind == System.Text.Json.JsonValueKind.Array)
            return DeserializeRows(inner);
        if (inner.ValueKind == System.Text.Json.JsonValueKind.Object &&
            inner.TryGetProperty("rows", out var rows))
            return DeserializeRows(rows);
        return null;
    }

    private static List<ApiRow>? DeserializeRows(System.Text.Json.JsonElement element)
    {
        var buffered = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(element);
        if (buffered == null) return null;
        var result = new List<ApiRow>();
        foreach (var item in buffered)
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue; // 跳过 $ref 引用串
            result.Add(item.Deserialize<ApiRow>()!);
        }
        return result;
    }

    private sealed class ApiEnvelope
    {
        public int code { get; set; }
        public System.Text.Json.JsonElement? data { get; set; }
        public List<ApiError>? errors { get; set; }
    }

    private sealed class ApiError
    {
        public string errorCode { get; set; } = "";
        public string msg { get; set; } = "";
    }

    private sealed class ApiRow
    {
        public string areaname { get; set; } = "";
        public string? areacode { get; set; }
        public string? displaycode { get; set; }
        public bool hasChildren { get; set; }
        public List<ApiRow>? children { get; set; }
    }
}
