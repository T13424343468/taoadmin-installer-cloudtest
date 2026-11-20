using System.Text;

namespace TaoAdmin.Common.Storage;

/// <summary>
/// MinIO 对象存储实现（S3 兼容协议，走 MinIO 自带 XML API，不引额外依赖）：
/// PutObject（流式上传）→ GetObject（读取）→ DeleteObject（删除）。
/// 未部署 MinIO 时不实例化本类；经 FileStorage:Provider 配置切换 Provider（local | minio）。
/// 注意：数据不出内网——Endpoint 指向内网 MinIO，禁止配置公有云 OSS。
/// </summary>
public class MinioFileStorageProvider : IFileStorageProvider
{
    private readonly string _endpoint;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _bucket;
    private readonly HttpClient _http;

    public MinioFileStorageProvider(string endpoint, string accessKey, string secretKey, string bucket, HttpClient? http = null)
    {
        _endpoint = endpoint.TrimEnd('/');
        _accessKey = accessKey;
        _secretKey = secretKey;
        _bucket = bucket;
        _http = http ?? new HttpClient();
    }

    public async Task<string> SaveAsync(Stream content, string originalName, string contentType)
    {
        var ext = Path.GetExtension(originalName);
        var now = DateTime.Now;
        // 对象键与本地盘实现同构：/uploads/{yyyy}/{MM}/{雪花ID}{ext}，业务侧无需区分 Provider
        var objectKey = $"uploads/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}";
        var url = $"{_endpoint}/{_bucket}/{objectKey}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = new StreamContent(content) };
        req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType);
        req.Headers.Add("x-amz-acl", "private");
        SignV4(req, objectKey);
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return $"/{objectKey}";
    }

    public async Task<Stream?> OpenAsync(string relativePath)
    {
        var objectKey = relativePath.TrimStart('/');
        var url = $"{_endpoint}/{_bucket}/{objectKey}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        SignV4(req, objectKey);
        using var resp = await _http.SendAsync(req);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStreamAsync() : null;
    }

    public async Task DeleteAsync(string relativePath)
    {
        var objectKey = relativePath.TrimStart('/');
        var url = $"{_endpoint}/{_bucket}/{objectKey}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        SignV4(req, objectKey);
        using var resp = await _http.SendAsync(req);
        // 删除不存在的对象在 S3 语义下也返回 204，无需特判
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>AWS Signature V4 签名（最小实现：单次 PUT/GET/DELETE，UNSIGNED-PAYLOAD）</summary>
    private void SignV4(HttpRequestMessage req, string objectKey)
    {
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = now.ToString("yyyyMMdd");
        var payloadHash = "UNSIGNED-PAYLOAD";

        var canonicalRequest = string.Join("\n",
            req.Method.Method, $"/{_bucket}/{objectKey}", "", $"host:{new Uri(_endpoint).Host}", "", "host", payloadHash);
        var scope = $"{dateStamp}/us-east-1/s3/aws4_request";
        var stringToSign = string.Join("\n", "AWS4-HMAC-SHA256", amzDate, scope,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))).ToLowerInvariant());

        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + _secretKey), dateStamp);
        var kRegion = HmacSha256(kDate, "us-east-1");
        var kService = HmacSha256(kRegion, "s3");
        var kSigning = HmacSha256(kService, "aws4_request");
        var signature = Convert.ToHexString(HmacSha256(kSigning, stringToSign)).ToLowerInvariant();

        req.Headers.Host = new Uri(_endpoint).Host;
        req.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        req.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        req.Headers.TryAddWithoutValidation("Authorization",
            $"AWS4-HMAC-SHA256 Credential={_accessKey}/{scope}, SignedHeaders=host, Signature={signature}");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var h = new System.Security.Cryptography.HMACSHA256(key);
        return h.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}
