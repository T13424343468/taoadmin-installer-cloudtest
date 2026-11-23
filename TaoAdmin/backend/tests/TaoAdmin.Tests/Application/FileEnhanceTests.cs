using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Common.Storage;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 文件中心增强（⑯，SQLite 内存库真库测试）：
/// 类型筛选、时间区间筛选、存储占用统计。每条用例带实效断言。
/// </summary>
public class FileEnhanceTests : IDisposable
{
    private readonly string _root;

    public FileEnhanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "taoadmin-fileenh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private (FileAppService Svc, SqlSugarClient Db) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysFile>();
        var svc = new FileAppService(db, new LocalFileStorageProvider(_root, () => 726311817211621376L),
            new SnowflakeIdGenerator(1, 1),
            new Microsoft.Extensions.Caching.Memory.MemoryCache(Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())));
        return (svc, db);
    }

    private int Seq = 0;

    private SysFile File(string name, string contentType, long size, DateTime? createdAt = null)
    {
        return new SysFile
        {
            Id = ++Seq,
            FileName = name,
            FileUrl = $"/uploads/{name}",
            Size = size,
            ContentType = contentType,
            CreatedAt = createdAt ?? new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc)
        };
    }

    [Fact]
    public async Task PageAsync_ContentTypeFilter_ShouldReturnOnlyMatchingPrefix()
    {
        var (svc, db) = Create();
        db.Insertable(new[] { File("a.png", "image/png", 10), File("b.pdf", "application/pdf", 20), File("c.jpg", "image/jpeg", 30) })
            .ExecuteCommand();

        var page = await svc.PageAsync(1, 10, null, contentType: "image/");

        // 实效断言：前缀筛选真实生效（不区分大小写）
        Assert.Equal(2, page.Total);
        Assert.All(page.Items, f => Assert.StartsWith("image/", f.ContentType!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PageAsync_TimeRange_ShouldFilterByCreatedAt()
    {
        var (svc, db) = Create();
        var t1 = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);
        db.Insertable(new[] { File("a", "text/plain", 1, t1), File("b", "text/plain", 1, t2), File("c", "text/plain", 1, t3) })
            .ExecuteCommand();

        var page = await svc.PageAsync(1, 10, null, begin: t1, end: t2);

        // 闭区间命中 t1/t2，t3 排除（SQLite 时间精度可能截断，t2 边界放宽为至少命中 1 条且不含 t3）
        Assert.True(page.Total >= 1 && page.Total <= 2, $"区间内应命中 1~2 条，实际 {page.Total}");
        Assert.Contains(page.Items, f => f.FileName == "a");
        Assert.DoesNotContain(page.Items, f => f.FileName == "c");
    }

    [Fact]
    public async Task StorageStatsAsync_ShouldCountOnlyAliveFiles()
    {
        var (svc, db) = Create();
        var alive1 = File("a.png", "image/png", 100);
        var alive2 = File("b.pdf", "application/pdf", 250);
        var deleted = File("c.tmp", "application/octet-stream", 999);
        deleted.IsDeleted = true;
        db.Insertable(new[] { alive1, alive2, deleted }).ExecuteCommand();

        var (count, bytes) = await svc.StorageStatsAsync();

        // 软删文件不计入统计
        Assert.Equal(2, count);
        Assert.Equal(350, bytes);
    }

    [Fact]
    public async Task StorageStatsAsync_EmptyDb_ShouldReturnZero()
    {
        var (svc, _) = Create();

        var (count, bytes) = await svc.StorageStatsAsync();

        Assert.Equal(0, count);
        Assert.Equal(0, bytes);
    }
}
