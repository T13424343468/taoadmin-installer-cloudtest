using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Common.Storage;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 文件中心覆盖补账（9.11 百分百全覆盖令）：游标翻页、参数钳制、
/// 空内容/空名/空类型/空用户、物理缺失读取、软删统计。每条用例带实效断言。
/// </summary>
public class FileAppServiceGapTests : IDisposable
{
    private readonly string _root;

    public FileAppServiceGapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "taoadmin-filegap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>记录型存储：透传本地盘实现，同时捕获保存参数（断言 contentType 兜底口径用）。</summary>
    private sealed class RecordingStorage : IFileStorageProvider
    {
        private readonly LocalFileStorageProvider _inner;
        public string? SavedContentType { get; private set; }

        public RecordingStorage(string root) => _inner = new LocalFileStorageProvider(root, () => 726311817211621376L);
        public async Task<string> SaveAsync(Stream content, string originalName, string contentType)
        {
            SavedContentType = contentType;
            return await _inner.SaveAsync(content, originalName, contentType);
        }
        public Task<Stream?> OpenAsync(string relativePath) => _inner.OpenAsync(relativePath);
        public Task DeleteAsync(string relativePath) => _inner.DeleteAsync(relativePath);
    }

    private (FileAppService Svc, SqlSugarClient Db) Create(IFileStorageProvider? storage = null)
    {
        // 唯一内存库名：避免与并行测试类共享连接池里的同一个 :memory: 库（测试隔离）
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = $"DataSource=file:taofilegap{Guid.NewGuid():N}?mode=memory",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysFile>();
        storage ??= new LocalFileStorageProvider(_root, () => 726311817211621376L);
        var svc = new FileAppService(db, storage, new SnowflakeIdGenerator(1, 1),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
        return (svc, db);
    }

    private static Stream Bytes(params byte[] data) => new MemoryStream(data);

    // ==================== 分页参数钳制 ====================

    [Fact]
    public async Task PageAsync_PageIndexBelowOne_ShouldClampToOne()
    {
        var (svc, _) = Create();
        await svc.UploadAsync(Bytes(1), "a.txt", "text/plain", 1, "admin");

        var page = await svc.PageAsync(0, 10, null);

        Assert.Equal(1, page.PageIndex); // 非法页码钳到 1，不抛不空
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task PageAsync_PageSizeOutOfRange_ShouldClampToTen()
    {
        var (svc, _) = Create();
        for (var i = 0; i < 3; i++)
            await svc.UploadAsync(Bytes((byte)i), $"f{i}.txt", "text/plain", 1, "admin");

        Assert.Equal(10, (await svc.PageAsync(1, 0, null)).PageSize);   // 过小钳到 10
        Assert.Equal(10, (await svc.PageAsync(1, 101, null)).PageSize); // 过大钳到 10
    }

    // ==================== 游标翻页 ====================

    [Fact]
    public async Task PageAsync_CursorPagination_ShouldEncodeAndWalkCursor()
    {
        var (svc, _) = Create();
        for (var i = 0; i < 3; i++)
            await svc.UploadAsync(Bytes((byte)i), $"c{i}.txt", "text/plain", 1, "admin");

        var page1 = await svc.PageAsync(1, 2, null);
        Assert.Equal(2, page1.Items.Count);
        Assert.False(string.IsNullOrEmpty(page1.NextCursor)); // 满页才发游标

        var page2 = await svc.PageAsync(1, 2, null, cursor: page1.NextCursor);
        Assert.Equal(1, page2.Items.Count);
        Assert.Null(page2.NextCursor); // 末页无下一页游标
        // 游标真实生效：两页不重复、合计覆盖全部记录
        Assert.DoesNotContain(page2.Items, f => page1.Items.Any(p => p.Id == f.Id));
    }

    [Fact]
    public async Task PageAsync_InvalidBase64Cursor_ShouldFallbackToOffset()
    {
        var (svc, _) = Create();
        await svc.UploadAsync(Bytes(1), "a.txt", "text/plain", 1, "admin");

        var page = await svc.PageAsync(1, 10, null, cursor: "@@@not-base64@@@");

        Assert.Equal(1, page.Total); // 解析失败按无游标处理，不抛不空
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task PageAsync_NonNumericCursor_ShouldIgnoreCursor()
    {
        var (svc, _) = Create();
        await svc.UploadAsync(Bytes(1), "a.txt", "text/plain", 1, "admin");

        var bogus = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("abc"));
        var page = await svc.PageAsync(1, 10, null, cursor: bogus);

        Assert.Equal(1, page.Total); // 非数字游标不参与过滤
        Assert.Single(page.Items);
    }

    // ==================== 下载与统计缺口 ====================

    [Fact]
    public async Task OpenAsync_PhysicalFileMissing_ShouldReturnNull()
    {
        var (svc, db) = Create();
        db.Insertable(new SysFile
        {
            Id = 1, FileName = "ghost.png", FileUrl = "/uploads/ghost.png",
            Size = 1, ContentType = "image/png", Md5 = "x", IsDeleted = false,
        }).ExecuteCommand();

        var opened = await svc.OpenAsync("/uploads/ghost.png");

        Assert.Null(opened); // 记录在、物理文件不在：不抛，返回空
    }

    [Fact]
    public async Task StorageStatsAsync_OnlySoftDeleted_ShouldReturnZero()
    {
        var (svc, db) = Create();
        var deleted = new SysFile
        {
            Id = 1, FileName = "d.png", FileUrl = "/uploads/d.png",
            Size = 999, ContentType = "image/png", Md5 = "x", IsDeleted = true,
        };
        db.Insertable(deleted).ExecuteCommand();

        var (count, bytes) = await svc.StorageStatsAsync();

        Assert.Equal(0, count);
        Assert.Equal(0, bytes); // 全软删：聚合空集兜底为 0
    }

    [Fact]
    public async Task StorageStatsAsync_WithLiveFiles_ShouldReturnRealTotals()
    {
        var (svc, db) = Create();
        db.Insertable(new SysFile
        {
            Id = 1, FileName = "a.png", FileUrl = "/uploads/a.png",
            Size = 100, ContentType = "image/png", Md5 = "x", IsDeleted = false,
        }).ExecuteCommand();
        db.Insertable(new SysFile
        {
            Id = 2, FileName = "b.png", FileUrl = "/uploads/b.png",
            Size = 250, ContentType = "image/png", Md5 = "y", IsDeleted = false,
        }).ExecuteCommand();

        var (count, bytes) = await svc.StorageStatsAsync();

        Assert.Equal(2, count);
        Assert.Equal(350, bytes); // 在册文件：真实笔数与字节总量
    }

    // ==================== 上传校验缺口 ====================

    [Fact]
    public async Task UploadAsync_NullContent_ShouldThrow()
    {
        var (svc, _) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(null!, "a.png", "image/png", 1, "admin"));
        Assert.Contains("不可读", ex.Message);
    }

    [Fact]
    public async Task UploadAsync_NullOrBlankName_ShouldThrow()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(Bytes(1), null, "image/png", 1, "admin"));
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(Bytes(1), "   ", "image/png", 1, "admin"));
    }

    [Fact]
    public async Task UploadAsync_EmptyContent_ShouldThrow()
    {
        var (svc, _) = Create();
        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(Bytes(), "empty.png", "image/png", 1, "admin"));
        Assert.Contains("内容为空", ex.Message);
    }

    [Fact]
    public async Task UploadAsync_NullContentType_ShouldSaveAsOctetStream()
    {
        var storage = new RecordingStorage(_root);
        var (svc, db) = Create(storage);

        var r = await svc.UploadAsync(Bytes(1, 2), "raw.txt", null, 1, "admin");

        Assert.Equal("application/octet-stream", storage.SavedContentType); // 存储层收到兜底类型
        Assert.Null(r.contentType); // 登记记录保留原始 null（前端按扩展名识别）
        Assert.Null(db.Queryable<SysFile>().Single().ContentType);
    }

    [Fact]
    public async Task UploadAsync_NullUserName_ShouldUseUserId()
    {
        var (svc, db) = Create();
        await svc.UploadAsync(Bytes(1), "a.txt", "text/plain", 42, null);
        Assert.Equal("42", db.Queryable<SysFile>().Single().UploadBy);

        await svc.UploadAsync(Bytes(2), "b.txt", "text/plain", 43, "   ");
        Assert.Equal("43", db.Queryable<SysFile>().ToList().Last().UploadBy); // 空白用户名同样回退 userId
    }
}
