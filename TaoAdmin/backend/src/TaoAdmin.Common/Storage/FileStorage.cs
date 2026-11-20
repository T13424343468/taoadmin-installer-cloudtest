namespace TaoAdmin.Common.Storage;

/// <summary>
/// 文件存储抽象：业务系统面向本接口使用文件能力，不关心文件最终落在哪。
/// 当前提供本地磁盘实现；未来可平滑替换为 MinIO / OSS 等对象存储，业务代码零改动。
/// </summary>
public interface IFileStorageProvider
{
    /// <summary>保存文件，返回可直接访问的相对路径（如 /uploads/2026/10/xxx.png），业务系统只需保存该路径</summary>
    Task<string> SaveAsync(Stream content, string originalName, string contentType);

    /// <summary>读取文件流（不存在返回 null）</summary>
    Task<Stream?> OpenAsync(string relativePath);

    /// <summary>删除物理文件</summary>
    Task DeleteAsync(string relativePath);
}

/// <summary>
/// 本地磁盘实现：按「年/月」分目录存放，文件名用雪花 ID 重命名（不信任用户输入的文件名）。
/// 存储根目录默认为 {contentRoot}/uploads，可通过 FileStorage:Root 配置。
/// </summary>
public class LocalFileStorageProvider : IFileStorageProvider
{
    private readonly string _root;
    private readonly Func<long> _idGenerator;

    public LocalFileStorageProvider(string root, Func<long> idGenerator)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        _idGenerator = idGenerator;
    }

    public async Task<string> SaveAsync(Stream content, string originalName, string contentType)
    {
        var ext = Path.GetExtension(originalName);
        var now = DateTime.Now;
        // 相对路径：/uploads/{yyyy}/{MM}/{雪花ID}{扩展名} —— 业务系统保存它，前端拼接域名即可访问
        var relative = $"/uploads/{now:yyyy}/{now:MM}/{_idGenerator()}{ext}";
        var physical = ToPhysical(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        await using var fs = File.Create(physical);
        await content.CopyToAsync(fs);
        return relative;
    }

    public Task<Stream?> OpenAsync(string relativePath)
    {
        var physical = ToPhysical(relativePath);
        // 防目录穿越：物理路径必须仍在存储根目录内
        return physical.StartsWith(_root) && File.Exists(physical)
            ? Task.FromResult<Stream?>(File.OpenRead(physical))
            : Task.FromResult<Stream?>(null);
    }

    public Task DeleteAsync(string relativePath)
    {
        var physical = ToPhysical(relativePath);
        if (physical.StartsWith(_root) && File.Exists(physical)) File.Delete(physical);
        return Task.CompletedTask;
    }

    private string ToPhysical(string relativePath)
        => Path.GetFullPath(relativePath.Replace("/uploads", _root, StringComparison.OrdinalIgnoreCase)
            .Replace('/', Path.DirectorySeparatorChar));
}
