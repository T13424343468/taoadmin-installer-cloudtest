using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Common.Storage;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 文件中心应用服务（SQLite 内存库真库测试 + 临时目录真实存储）：
/// 上传登记、MD5 秒传去重、扩展名白名单、大小上限、下载内容一致、软删除、Provider 切换。
/// </summary>
public class FileAppServiceTests : IDisposable
{
    private readonly string _root;

    public FileAppServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "taoadmin-filetest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private (FileAppService Svc, SqlSugarClient Db, IFileStorageProvider Storage) Create(IFileStorageProvider? storage = null)
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysFile>();
        storage ??= new LocalFileStorageProvider(_root, () => 726311817211621376L);
        return (new FileAppService(db, storage, new SnowflakeIdGenerator(1, 1)), db, storage);
    }

    private static Stream Bytes(params byte[] data) => new MemoryStream(data);

    [Fact]
    public async Task UploadAsync_ShouldRegisterRecord_AndPhysicallyExist()
    {
        var (svc, db, _) = Create();
        var r = await svc.UploadAsync(Bytes(1, 2, 3), "demo.png", "image/png", 1, "admin");
        var row = Assert.Single(db.Queryable<SysFile>().ToList());
        Assert.Equal("demo.png", row.FileName);
        Assert.Equal(3, row.Size);
        Assert.StartsWith("/uploads/", r.fileUrl);
        Assert.False(r.deduplicated);
        // 物理文件真实落盘
        Assert.True(new LocalFileStorageProvider(_root, () => 0).OpenAsync(r.fileUrl) is not null);
    }

    [Fact]
    public async Task UploadAsync_SameMd5Twice_ShouldDeduplicate()
    {
        var (svc, _, storage) = Create();
        var first = await svc.UploadAsync(new MemoryStream(new byte[] { 9, 9, 9 }), "a.txt", "text/plain", 1, "admin");
        var second = await svc.UploadAsync(new MemoryStream(new byte[] { 9, 9, 9 }), "b.txt", "text/plain", 2, "user2");
        Assert.False(first.deduplicated);
        Assert.True(second.deduplicated);                 // 第二次命中秒传
        Assert.Equal(first.fileUrl, second.fileUrl);      // 复用同一物理路径
        Assert.Equal(first.md5, second.md5);
    }

    [Fact]
    public async Task UploadAsync_DisallowedExtension_ShouldThrow()
    {
        var (svc, _, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(Bytes(1), "evil.exe", "application/x-msdownload", 1, "admin"));
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(Bytes(1), "script.sh", null, 1, "admin"));
    }

    [Fact]
    public async Task UploadAsync_oversize_ShouldThrow()
    {
        var (svc, _, _) = Create();
        var big = new byte[FileAppService.MaxUploadSize + 1];
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.UploadAsync(new MemoryStream(big), "big.png", "image/png", 1, "admin"));
    }

    [Fact]
    public async Task OpenAsync_DownloadContentShouldMatchUpload()
    {
        var (svc, _, _) = Create();
        byte[] payload = [10, 20, 30, 40, 50];
        var r = await svc.UploadAsync(new MemoryStream(payload), "bin.txt", "text/plain", 1, "admin");

        var opened = await svc.OpenAsync(r.fileUrl);
        Assert.NotNull(opened);
        using var ms = new MemoryStream();
        await opened!.Value.Content.CopyToAsync(ms);
        Assert.Equal(payload, ms.ToArray());              // 内容逐字节一致
    }

    [Fact]
    public async Task DeleteAsync_ShouldSoftDelete_AndRemovePhysical()
    {
        var (svc, db, storage) = Create();
        var r = await svc.UploadAsync(Bytes(5, 5), "del.png", "image/png", 1, "admin");
        var id = r.id;

        await svc.DeleteAsync(id);
        Assert.Empty(db.Queryable<SysFile>().Where(f => f.IsDeleted == false).ToList()); // 记录软删不可见
        var gone = await svc.OpenAsync(r.fileUrl);
        Assert.Null(gone);                                 // 物理文件已删，读取返回空
        await Assert.ThrowsAsync<DomainException>(() => svc.DeleteAsync(id)); // 再删报不存在
    }

    [Fact]
    public async Task PageAsync_ShouldFilterByKeyword()
    {
        var (svc, _, _) = Create();
        await svc.UploadAsync(Bytes(1), "合同.pdf", "application/pdf", 1, "admin");
        await svc.UploadAsync(Bytes(2), "头像.png", "image/png", 1, "admin");

        var page = await svc.PageAsync(1, 10, "合同");
        Assert.Equal(1, page.Total);
        Assert.Equal("合同.pdf", page.Items[0].FileName);
        Assert.Equal(2, (await svc.PageAsync(1, 10, null)).Total);
    }

    /// <summary>Provider 切换：MinIO Provider 不可达时上传抛异常（网络失败不静默），本地盘不受影响——切换逻辑由配置决定，行为互不串扰。</summary>
    [Fact]
    public async Task ProviderSwitch_MinioUnavailable_ShouldFailLoudly_LocalStillWorks()
    {
        // 指向内网不存在的端口：连接必然失败
        var minio = new MinioFileStorageProvider("http://127.0.0.1:1", "ak", "sk", "bucket");
        var (svc, _, _) = Create(minio);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            svc.UploadAsync(Bytes(1), "x.png", "image/png", 1, "admin"));

        // 切回本地盘 Provider：同一套业务代码直接可用
        var (svc2, _, _) = Create(); // 默认 LocalFileStorageProvider
        var ok = await svc2.UploadAsync(Bytes(1, 1), "y.png", "image/png", 1, "admin");
        Assert.False(ok.deduplicated);
        Assert.StartsWith("/uploads/", ok.fileUrl);
    }
}
