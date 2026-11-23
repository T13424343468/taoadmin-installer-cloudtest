using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using TaoFiles.Api.Storage;
using Xunit;

namespace TaoFiles.Tests.Storage;

/// <summary>
/// 上传体积上限（⑬）：站点配置优先（ApiResult.data 包装解析）、
/// HTTP 失败/无 URL/非法值回退本地配置、60 秒缓存不重复请求；
/// 中间件动态收紧请求体上限、无 Feature 不抛出；业务层超限中文报错。
/// </summary>
public class UploadLimitTests
{
    /// <summary>脚本化响应桩：可预设响应或抛错，并统计请求次数。</summary>
    private sealed class ScriptedHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int CallCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responder());
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { Timeout = TimeSpan.FromSeconds(3) };
    }

    private static UploadLimitProvider Create(
        Func<HttpResponseMessage> responder,
        string? siteConfigUrl,
        string? localMaxMB,
        IMemoryCache? cache,
        out ScriptedHandler? handlerRef)
    {
        var handler = new ScriptedHandler(responder);
        handlerRef = handler;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TaoFiles:SiteConfigUrl"] = siteConfigUrl,
            ["FileStorage:MaxSizeMB"] = localMaxMB
        }).Build();
        return new UploadLimitProvider(config, new StubFactory(handler), cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLogger<UploadLimitProvider>.Instance);
    }

    private const string ApiResultBody = """{"code":200,"success":true,"data":{"siteName":"TaoAdmin","uploadMaxSizeMB":100}}""";

    private static HttpResponseMessage JsonOk(string body) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task GetLimitBytesAsync_站点配置成功_按data节点解析返回配置值()
    {
        var svc = Create(() => JsonOk(ApiResultBody), "http://localhost:5001/site-config", "50", null, out _);
        // 100MB = 104857600 字节
        Assert.Equal(100L * 1024 * 1024, await svc.GetLimitBytesAsync());
    }

    [Fact]
    public async Task GetLimitBytesAsync_站点配置HTTP失败_回退本地配置()
    {
        var svc = Create(() => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable),
            "http://localhost:5001/site-config", "50", null, out _);
        Assert.Equal(50L * 1024 * 1024, await svc.GetLimitBytesAsync());
    }

    [Fact]
    public async Task GetLimitBytesAsync_站点配置请求异常_回退本地配置()
    {
        var svc = Create(() => throw new HttpRequestException("连接失败"),
            "http://localhost:5001/site-config", "50", null, out _);
        Assert.Equal(50L * 1024 * 1024, await svc.GetLimitBytesAsync());
    }

    [Fact]
    public async Task GetLimitBytesAsync_未配置站点配置地址_回退本地配置不发请求()
    {
        var svc = Create(() => throw new InvalidOperationException("不应发起请求"), null, "50", null, out var handler);
        Assert.Equal(50L * 1024 * 1024, await svc.GetLimitBytesAsync());
        Assert.Equal(0, handler!.CallCount);
    }

    [Fact]
    public async Task GetLimitBytesAsync_站点配置非法值_回退本地配置()
    {
        var svc = Create(() => JsonOk("""{"code":200,"success":true,"data":{"uploadMaxSizeMB":-5}}"""),
            "http://localhost:5001/site-config", "50", null, out _);
        Assert.Equal(50L * 1024 * 1024, await svc.GetLimitBytesAsync());
    }

    [Fact]
    public async Task GetLimitBytesAsync_响应缺data节点_回退本地配置()
    {
        var svc = Create(() => JsonOk("""{"code":200,"success":true}"""),
            "http://localhost:5001/site-config", "50", null, out _);
        Assert.Equal(50L * 1024 * 1024, await svc.GetLimitBytesAsync());
    }

    [Fact]
    public async Task GetLimitBytesAsync_data缺上限字段_回退本地配置()
    {
        var svc = Create(() => JsonOk("""{"code":200,"success":true,"data":{"siteName":"TaoAdmin"}}"""),
            "http://localhost:5001/site-config", "50", null, out _);
        Assert.Equal(50L * 1024 * 1024, await svc.GetLimitBytesAsync());
    }

    [Fact]
    public async Task GetLimitBytesAsync_缓存命中_不重复发起请求()
    {
        var svc = Create(() => JsonOk(ApiResultBody), "http://localhost:5001/site-config", "50",
            new MemoryCache(new MemoryCacheOptions()), out var handler);
        Assert.Equal(100L * 1024 * 1024, await svc.GetLimitBytesAsync());
        Assert.Equal(100L * 1024 * 1024, await svc.GetLimitBytesAsync());
        Assert.Equal(1, handler!.CallCount); // 第二次走缓存
    }

    // ==================== 中间件 ====================

    [Fact]
    public async Task Invoke_有请求体上限Feature_按上限加表单余量设置()
    {
        var limitProvider = Create(() => JsonOk(ApiResultBody), "http://localhost:5001/site-config", "50", null, out _);
        var feature = new MockMaxRequestBodySizeFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        var called = false;
        await new UploadLimitMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, limitProvider).InvokeAsync(context);

        Assert.True(called);
        Assert.Equal(100L * 1024 * 1024 + UploadLimitMiddleware.FormOverheadBytes, feature.MaxRequestBodySize);
    }

    [Fact]
    public async Task Invoke_无请求体上限Feature_不抛出继续管道()
    {
        var limitProvider = Create(() => JsonOk(ApiResultBody), "http://localhost:5001/site-config", "50", null, out _);
        var context = new DefaultHttpContext(); // 测试环境无 Kestrel Feature

        var called = false;
        await new UploadLimitMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, limitProvider).InvokeAsync(context);

        Assert.True(called);
    }

    private sealed class MockMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}

/// <summary>文件上传体积拦截（业务层兜底）：超限返回明确中文报错。</summary>
public class FileUploadLimitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "taofiles-limit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class FixedLimitProvider(long limitBytes) : IUploadLimitProvider
    {
        public Task<long> GetLimitBytesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(limitBytes);
    }

    private sealed class StubFormFile(Stream content, string fileName) : IFormFile
    {
        public Stream OpenReadStream() => content;
        public long Length => content.Length;
        public string Name => "file";
        public string FileName => fileName;
        public string ContentType => "application/octet-stream";
        public string ContentDisposition => null!;
        public IHeaderDictionary Headers => new HeaderDictionary();
        public void CopyTo(Stream target) => content.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => content.CopyToAsync(target, cancellationToken);
    }

    [Fact]
    public async Task UploadAsync_文件超限_抛明确中文报错()
    {
        Directory.CreateDirectory(_root);
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<TaoFiles.Api.Entities.SysFile>();
        var storage = new LocalFileStorageProvider(_root, () => 1710000000000000000L);
        var svc = new TaoFiles.Api.Controllers.FileAppService(
            db, storage, new FixedLimitProvider(1L * 1024 * 1024), new MemoryCache(new MemoryCacheOptions()));

        // 2MB 文件，上限 1MB → 拦截
        await using var content = new MemoryStream(new byte[2 * 1024 * 1024]);
        var ex = await Assert.ThrowsAsync<TaoFiles.Api.Controllers.DomainException>(
            () => svc.UploadAsync(new StubFormFile(content, "big.png"), null, null));
        Assert.Equal(400, ex.Code);
        Assert.Contains("超过上传上限", ex.Message);
        Assert.Contains("1MB", ex.Message);
        Assert.Contains("2MB", ex.Message);
    }

    [Fact]
    public async Task UploadAsync_文件未超限_正常上传()
    {
        Directory.CreateDirectory(_root);
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<TaoFiles.Api.Entities.SysFile>();
        var storage = new LocalFileStorageProvider(_root, () => 1710000000000000000L);
        var svc = new TaoFiles.Api.Controllers.FileAppService(
            db, storage, new FixedLimitProvider(1L * 1024 * 1024), new MemoryCache(new MemoryCacheOptions()));

        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var result = await svc.UploadAsync(new StubFormFile(content, "ok.png"), null, null);
        Assert.False(result.reused);
        Assert.Equal(3, result.size);
    }
}
