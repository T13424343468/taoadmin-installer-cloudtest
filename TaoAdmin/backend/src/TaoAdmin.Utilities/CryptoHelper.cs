using System.Security.Cryptography;
using System.Text;

namespace TaoAdmin.Utilities;

/// <summary>
/// 加解密工具：MD5 / SHA / HMAC / AES（对称）/ RSA（非对称）。
/// </summary>
public static class CryptoHelper
{
    // ---------- 哈希 ----------

    public static string Md5(string input)
        => Convert.ToHexString(MD5.HashData(input.ToBytes()));

    public static string Sha256(string input)
        => Convert.ToHexString(SHA256.HashData(input.ToBytes()));

    public static string HmacSha256(string input, string key)
        => Convert.ToHexString(new HMACSHA256(key.ToBytes()).ComputeHash(input.ToBytes()));

    // ---------- AES（AES-256-CBC，默认独立随机 IV 拼在密文前） ----------

    public static string AesEncrypt(string plainText, string key)
    {
        var keyBytes = PaddedKey(key, 32);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plainText.ToBytes(), 0, plainText.ToBytes().Length);
        var result = new byte[aes.IV.Length + cipher.Length];
        aes.IV.CopyTo(result, 0);
        cipher.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    public static string AesDecrypt(string cipherBase64, string key)
    {
        var data = Convert.FromBase64String(cipherBase64);
        var keyBytes = PaddedKey(key, 32);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = data[..16];
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(data[16..], 0, data.Length - 16);
        return Encoding.UTF8.GetString(plain);
    }

    // ---------- RSA（签名与加解密） ----------

    /// <summary>生成 RSA 密钥对（XML 格式）</summary>
    public static (string publicKey, string privateKey) RsaGenerateKeyPair(int keySize = 2048)
    {
        using var rsa = RSA.Create(keySize);
        return (rsa.ToXmlString(false), rsa.ToXmlString(true));
    }

    public static string RsaSign(string data, string privateKeyXml)
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(privateKeyXml);
        return Convert.ToBase64String(rsa.SignData(data.ToBytes(), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    public static bool RsaVerify(string data, string sign, string publicKeyXml)
    {
        using var rsa = RSA.Create();
        rsa.FromXmlString(publicKeyXml);
        return rsa.VerifyData(data.ToBytes(), Convert.FromBase64String(sign), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    private static byte[] PaddedKey(string key, int length)
    {
        var bytes = key.ToBytes();
        if (bytes.Length >= length) return bytes[..length];
        return bytes.Concat(new byte[length - bytes.Length]).ToArray();
    }
}
