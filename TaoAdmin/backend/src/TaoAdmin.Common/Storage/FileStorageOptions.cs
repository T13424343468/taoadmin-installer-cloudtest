namespace TaoAdmin.Common.Storage;

/// <summary>
/// 文件存储配置（appsettings 节 FileStorage）：
/// Provider = local（默认，本地磁盘）| minio（S3 兼容对象存储，数据不出内网）。
/// Root 仅 local 生效；Minio 四项仅 minio 生效。
/// </summary>
public class FileStorageOptions
{
    /// <summary>存储实现：local | minio</summary>
    public string Provider { get; set; } = "local";

    /// <summary>本地盘存储根目录（相对 contentRoot）</summary>
    public string Root { get; set; } = "uploads";

    /// <summary>MinIO 连接参数</summary>
    public MinioOptions Minio { get; set; } = new();
}

/// <summary>MinIO 连接参数（Endpoint 指向内网对象存储，禁配公有云 OSS）</summary>
public class MinioOptions
{
    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "taoadmin";
}
