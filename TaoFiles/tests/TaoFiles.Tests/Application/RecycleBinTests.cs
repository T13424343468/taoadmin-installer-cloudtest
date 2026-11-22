using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using TaoFiles.Api.Controllers;
using TaoFiles.Api.Entities;
using TaoFiles.Api.Storage;
using Xunit;

namespace TaoFiles.Tests.Application;

/// <summary>
/// 文件回收站（SQLite 内存库真库测试 + 临时目录真实存储）：
/// 删除进回收站且磁盘保留、回收站分页、恢复还原、彻底删除清记录清磁盘、
/// 非回收站记录操作拒绝、软删后同内容重传不受影响。
/// </summary>
public class RecycleBinTests : IDisposable
{
    private readonly string _root;

    public RecycleBinTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "taofiles-recycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private (FileAppService Svc, SqlSugarClient Db, LocalFileStorageProvider Storage) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysFile>();
        var storage = new LocalFileStorageProvider(_root, () => 1710000000000000000L);
        var config = new ConfigurationBuilder().Build();
        return (new FileAppService(db, storage, config, new MemoryCache(new MemoryCacheOptions())), db, storage);
    }

    /// <summary>造一条已落盘、已登记的文件记录，返回（记录, 磁盘相对路径）</summary>
    private static async Task<(SysFile Entity, string Path)> SeedAsync(FileAppService svc, SqlSugarClient db, LocalFileStorageProvider storage, string name)
    {
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var path = await storage.SaveAsync(content, name);
        var entity = new SysFile
        {
            Id = 1710000000000000001L + name.Length,
            FileName = name,
            FileUrl = path,
            Size = 3,
            Md5 = name.GetHashCode().ToString("X")
        };
        await db.Insertable(entity).ExecuteCommandAsync();
        return (entity, path);
    }

    [Fact]
    public async Task Delete_ShouldMoveToRecycleBin_AndKeepDiskFile()
    {
        var (svc, db, storage) = Create();
        var (entity, path) = await SeedAsync(svc, db, storage, "a.png");

        await svc.DeleteAsync(entity.Id.ToString());

        var row = await db.Queryable<SysFile>().FirstAsync(f => f.Id == entity.Id);
        Assert.True(row!.IsDeleted);
        Assert.NotNull(row.DeletedAt);
        // 磁盘文件保留：软删阶段不物理删除（删除可还原必留退路）
        Assert.NotNull(await storage.OpenAsync(path));
        // 正常列表不出现，回收站列表出现
        Assert.Empty((await svc.PageAsync(1, 10, null, null)).list);
        var bin = await svc.RecycleBinPageAsync(1, 10);
        Assert.Equal(1, bin.total);
        Assert.Equal(entity.FileName, bin.list[0].fileName);
        Assert.NotNull(bin.list[0].deletedAt);
    }

    [Fact]
    public async Task Restore_ShouldReturnFileToNormalList()
    {
        var (svc, db, storage) = Create();
        var (entity, path) = await SeedAsync(svc, db, storage, "b.png");
        await svc.DeleteAsync(entity.Id.ToString());

        await svc.RestoreAsync(entity.Id.ToString());

        var row = await db.Queryable<SysFile>().FirstAsync(f => f.Id == entity.Id);
        Assert.False(row!.IsDeleted);
        Assert.Null(row.DeletedAt);
        Assert.Single((await svc.PageAsync(1, 10, null, null)).list);
        Assert.Equal(0, (await svc.RecycleBinPageAsync(1, 10)).total);
        // 恢复后磁盘文件仍可读（从未删除）
        Assert.NotNull(await storage.OpenAsync(path));
    }

    [Fact]
    public async Task Purge_ShouldRemoveRow_AndDiskFile()
    {
        var (svc, db, storage) = Create();
        var (entity, path) = await SeedAsync(svc, db, storage, "c.png");
        await svc.DeleteAsync(entity.Id.ToString());

        await svc.PurgeAsync(entity.Id.ToString());

        Assert.Null(await db.Queryable<SysFile>().FirstAsync(f => f.Id == entity.Id));
        Assert.Null(await storage.OpenAsync(path));
        Assert.Equal(0, (await svc.RecycleBinPageAsync(1, 10)).total);
    }

    [Fact]
    public async Task RestoreOrPurge_NotInRecycleBin_ShouldThrow404()
    {
        var (svc, db, storage) = Create();
        var (entity, _) = await SeedAsync(svc, db, storage, "d.png");

        // 未删除的记录不在回收站，恢复/彻底删除一律 404
        var restoreEx = await Assert.ThrowsAsync<DomainException>(
            () => svc.RestoreAsync(entity.Id.ToString()));
        Assert.Equal(404, restoreEx.Code);
        var purgeEx = await Assert.ThrowsAsync<DomainException>(
            () => svc.PurgeAsync(entity.Id.ToString()));
        Assert.Equal(404, purgeEx.Code);
        // 不存在的 ID 同样 404
        var missingEx = await Assert.ThrowsAsync<DomainException>(() => svc.PurgeAsync("999"));
        Assert.Equal(404, missingEx.Code);
    }

    [Fact]
    public async Task Upload_SameContent_AfterSoftDelete_ShouldCreateNewRecord()
    {
        var (svc, db, storage) = Create();
        var (entity, path) = await SeedAsync(svc, db, storage, "e.png");
        await svc.DeleteAsync(entity.Id.ToString());

        // 回收站中的记录不参与去重：同内容重传落新记录新物理文件，与回收站记录互不影响
        await using var again = new MemoryStream(new byte[] { 1, 2, 3 });
        var form = new StubFormFile(again, "e.png");
        var result = await svc.UploadAsync(form, null, null);

        Assert.False(result.reused);
        Assert.NotEqual(entity.Id.ToString(), result.id);
        // 回收站里的原记录与其磁盘文件原样保留
        var recycled = await db.Queryable<SysFile>().FirstAsync(f => f.Id == entity.Id);
        Assert.True(recycled!.IsDeleted);
        Assert.NotNull(await storage.OpenAsync(path));
    }

    /// <summary>最小 IFormFile 桩：FormFile 类型在测试环境依赖 HttpContext 不可用，上传去重只需流/文件名/长度</summary>
    private sealed class StubFormFile(Stream content, string fileName) : IFormFile
    {
        public Stream OpenReadStream() => content;
        public long Length => content.Length;
        public string Name => "file";
        public string FileName => fileName;
        public string ContentType => "application/octet-stream";
        public string? ContentDisposition => null;
        public IHeaderDictionary Headers => new HeaderDictionary();
        public void CopyTo(Stream target) => content.CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => content.CopyToAsync(target, cancellationToken);
    }
}
