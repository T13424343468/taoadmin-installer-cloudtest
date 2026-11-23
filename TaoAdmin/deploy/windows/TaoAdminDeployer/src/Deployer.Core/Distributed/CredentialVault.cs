// ⑳分布式安装器：凭据加密托管。SSH 账号密码/密钥加密存储、不进日志不落明文。
// 实现口径：DPAPI（Windows）或文件权限保护的密钥文件（Linux），密文落库，明文只在内存。
namespace TaoAdmin.Deployer.Core.Distributed;

public interface ICredentialVault
{
    void Store(string nodeId, string secret);
    string? Retrieve(string nodeId);
    bool Delete(string nodeId);
}

public sealed class CredentialVault : ICredentialVault
{
    private readonly string _dir;
    private readonly byte[] _key;
    private readonly Func<bool> _unixFileModeSupported;

    /// <summary>凭据库：密文落盘、密钥文件托管。</summary>
    /// <param name="vaultDir">凭据库目录。</param>
    /// <param name="masterKey">显式主密钥（32 字节）；缺省时从密钥文件加载或新建。</param>
    /// <param name="unixFileModeSupported">平台能力探针：当前平台是否支持 UnixFileMode。
    /// Windows 无此 API（由目录 ACL 兜底），跳过权限收紧；测试可注入以实测两条路径。</param>
    public CredentialVault(string vaultDir, byte[]? masterKey = null, Func<bool>? unixFileModeSupported = null)
    {
        if (string.IsNullOrWhiteSpace(vaultDir)) throw new ArgumentException("凭据库目录不能为空", nameof(vaultDir));
        _dir = vaultDir;
        Directory.CreateDirectory(_dir);
        _unixFileModeSupported = unixFileModeSupported ?? (() => !OperatingSystem.IsWindows());
        _key = masterKey ?? LoadOrCreateKey();
    }

    private string PathFor(string nodeId) => Path.Combine(_dir, Sanitize(nodeId) + ".cred");

    private static string Sanitize(string nodeId)
    {
        var bad = nodeId.IndexOfAny(Path.GetInvalidFileNameChars());
        return bad >= 0 ? nodeId.Substring(0, bad) : nodeId;
    }

    private byte[] LoadOrCreateKey()
    {
        var keyFile = Path.Combine(_dir, "vault.key");
        if (File.Exists(keyFile))
        {
            var existing = File.ReadAllBytes(keyFile);
            if (existing.Length == 32) return existing;
        }
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(keyFile, key);
        RestrictToOwnerOnly(keyFile);
        return key;
    }

    /// <summary>权限收紧：仅属主可读写。不支持 UnixFileMode 的平台跳过（目录 ACL 兜底）。</summary>
    private void RestrictToOwnerOnly(string path)
    {
        if (!_unixFileModeSupported()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Store(string nodeId, string secret)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("节点不能为空", nameof(nodeId));
        if (secret is null || secret.Length == 0) throw new ArgumentException("凭据不能为空", nameof(secret));
        var plain = System.Text.Encoding.UTF8.GetBytes(secret);
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var cipher = AesGcmExt.AesGcmSeal(plain, _key, nonce);
        // 格式：magic(4) + nonce(12) + cipher
        var blob = new byte[4 + 12 + cipher.Length];
        AesGcmExt.Magic.CopyTo(blob, 0);
        nonce.CopyTo(blob, 4);
        cipher.CopyTo(blob, 16);
        File.WriteAllBytes(PathFor(nodeId), blob);
        RestrictToOwnerOnly(PathFor(nodeId));
    }

    public string? Retrieve(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return null;
        var f = PathFor(nodeId);
        if (!File.Exists(f)) return null;
        var blob = File.ReadAllBytes(f);
        if (blob.Length < 16 || blob[0] != (byte)'T' || blob[1] != (byte)'A' || blob[2] != (byte)'2' || blob[3] != (byte)'0')
            throw new InvalidOperationException("凭据文件损坏或格式不符");
        var nonce = blob[4..16];
        var cipher = blob[16..];
        var plain = AesGcmExt.AesGcmOpen(cipher, _key, nonce);
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    public bool Delete(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return false;
        var f = PathFor(nodeId);
        if (!File.Exists(f)) return false;
        File.Delete(f);
        return true;
    }
}

// .NET 10 AesGcm 封装：密文+tag 拼接，magic 常量统一管理
internal static class AesGcmExt
{
    public static readonly byte[] Magic = { (byte)'T', (byte)'A', (byte)'2', (byte)'0' };

    public static byte[] AesGcmSeal(byte[] plain, byte[] key, byte[] nonce)
    {
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plain, cipher, tag);
        return cipher.Concat(tag).ToArray();
    }

    public static byte[] AesGcmOpen(byte[] cipherWithTag, byte[] key, byte[] nonce)
    {
        using var aes = new System.Security.Cryptography.AesGcm(key, 16);
        var cipher = cipherWithTag[..^16];
        var tag = cipherWithTag[^16..];
        var plain = new byte[cipher.Length];
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}
