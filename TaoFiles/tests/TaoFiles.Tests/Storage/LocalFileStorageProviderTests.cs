using TaoFiles.Api.Storage;
using Xunit;

namespace TaoFiles.Tests.Storage;

/// <summary>本地文件存储：保存 / 读取 / 删除 / 路径越界防护</summary>
public class LocalFileStorageProviderTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileStorageProvider _provider;
    private readonly Func<long> _idGen = () => 1710000000000000000L;

    public LocalFileStorageProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "taofiles-tests", Guid.NewGuid().ToString("N"));
        _provider = new LocalFileStorageProvider(_root, _idGen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_ShouldReturnRelativePath_WithOriginalExtension()
    {
        await using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        var path = await _provider.SaveAsync(content, "photo.png");
        Assert.Matches(@"^/uploads/\d{4}/\d{2}/1710000000000000000\.png$", path);
    }

    [Fact]
    public async Task SaveAsync_ThenOpen_ShouldReturnIdenticalContent()
    {
        var payload = new byte[] { 72, 101, 108, 108, 111 };
        await using (var content = new MemoryStream(payload))
            await _provider.SaveAsync(content, "a.txt");

        await using var stream = await _provider.OpenAsync("/uploads/" + DateTime.Now.Year + "/" + DateTime.Now.ToString("MM") + "/1710000000000000000.txt");
        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        Assert.Equal(payload, ms.ToArray());
    }

    [Fact]
    public async Task OpenAsync_MissingFile_ShouldReturnNull()
        => Assert.Null(await _provider.OpenAsync("/uploads/2026/01/none.txt"));

    [Fact]
    public async Task DeleteAsync_ShouldRemovePhysicalFile()
    {
        await using (var content = new MemoryStream(new byte[] { 9 }))
            await _provider.SaveAsync(content, "d.dat");

        var now = DateTime.Now;
        var path = $"/uploads/{now:yyyy}/{now:MM}/1710000000000000000.dat";
        Assert.NotNull(await _provider.OpenAsync(path));

        await _provider.DeleteAsync(path);
        Assert.Null(await _provider.OpenAsync(path));
    }

    [Fact]
    public async Task OpenAsync_ParentTraversal_ShouldBeBlocked()
    {
        // 相对路径带 ../ 越界：物理路径解析后超出存储根目录 → 拒绝
        var outside = Path.Combine(Path.GetTempPath(), "taofiles-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "secret");

        try
        {
            var result = await _provider.OpenAsync("/uploads/../../" + Path.GetFileName(outside));
            Assert.Null(result);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task SaveAsync_UnknownExtension_ShouldStillStore_ExtensionUntouched()
    {
        // 扩展名白名单是应用服务层职责，存储层原样保留扩展名（不信任文件名主体）
        await using var content = new MemoryStream(new byte[] { 1 });
        var path = await _provider.SaveAsync(content, "evil<script>.txt");
        Assert.EndsWith(".txt", path);
        Assert.DoesNotContain("<", Path.GetFileName(path));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentCalls_ShouldReturnDistinctPaths_WhenIdGeneratorUnique()
    {
        var counter = 0L;
        var provider = new LocalFileStorageProvider(_root, () => Interlocked.Increment(ref counter));
        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var c = new MemoryStream(new byte[] { 1 });
            return await provider.SaveAsync(c, "x.txt");
        });
        var paths = await Task.WhenAll(tasks);
        Assert.Equal(paths.Length, paths.Distinct().Count());
    }
}
