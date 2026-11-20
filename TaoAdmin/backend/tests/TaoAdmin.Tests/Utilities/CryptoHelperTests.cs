using System.Security.Cryptography;
using TaoAdmin.Utilities;
using Xunit;

namespace TaoAdmin.Tests.Utilities;

/// <summary>加解密工具：哈希已知向量、AES 往返、RSA 签验</summary>
public class CryptoHelperTests
{
    [Theory]
    [InlineData("abc", "900150983CD24FB0D6963F7D28E17F72")]
    [InlineData("", "D41D8CD98F00B204E9800998ECF8427E")]
    public void Md5_KnownVectors(string input, string expected)
        => Assert.Equal(expected, CryptoHelper.Md5(input));

    [Fact]
    public void Sha256_KnownVector()
        => Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            CryptoHelper.Sha256("abc"));

    [Fact]
    public void HmacSha256_KnownVector()
    {
        // RFC 6234 风格经典测试向量
        Assert.Equal("F7BC83F430538424B13298E6AA6FB143EF4D59A14946175997479DBC2D1A3CD8",
            CryptoHelper.HmacSha256("The quick brown fox jumps over the lazy dog", "key"));
    }

    [Fact]
    public void AesEncryptDecrypt_Roundtrip_ShouldRecoverPlaintext()
    {
        const string key = "my-secret-key";
        const string plain = "你好，TaoAdmin! Hello 123 ✓";
        var cipher = CryptoHelper.AesEncrypt(plain, key);

        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, CryptoHelper.AesDecrypt(cipher, key));
    }

    [Fact]
    public void AesEncrypt_SameInputTwice_ShouldProduceDifferentCipherText()
    {
        // 每次随机 IV，相同明文密文不同
        var a = CryptoHelper.AesEncrypt("same", "k");
        var b = CryptoHelper.AesEncrypt("same", "k");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AesDecrypt_WithWrongKey_ShouldFail()
    {
        var cipher = CryptoHelper.AesEncrypt("secret", "correct-key");
        Assert.ThrowsAny<CryptographicException>(() => CryptoHelper.AesDecrypt(cipher, "wrong-key"));
    }

    [Fact]
    public void Aes_LongKey_ShouldBeTruncatedTo32Bytes()
    {
        var longKey = new string('K', 64);
        var cipher = CryptoHelper.AesEncrypt("data", longKey);
        Assert.Equal("data", CryptoHelper.AesDecrypt(cipher, longKey));
    }

    [Fact]
    public void RsaSignAndVerify_ShouldMatch()
    {
        var (publicKey, privateKey) = CryptoHelper.RsaGenerateKeyPair(2048);
        var sign = CryptoHelper.RsaSign("payload", privateKey);
        Assert.True(CryptoHelper.RsaVerify("payload", sign, publicKey));
    }

    [Fact]
    public void RsaVerify_TamperedData_ShouldFail()
    {
        var (publicKey, privateKey) = CryptoHelper.RsaGenerateKeyPair(2048);
        var sign = CryptoHelper.RsaSign("payload", privateKey);
        Assert.False(CryptoHelper.RsaVerify("payload-tampered", sign, publicKey));
    }

    [Fact]
    public void RsaVerify_TamperedSignature_ShouldFail()
    {
        var (publicKey, privateKey) = CryptoHelper.RsaGenerateKeyPair(2048);
        var sign = CryptoHelper.RsaSign("payload", privateKey);
        var bytes = Convert.FromBase64String(sign);
        bytes[0] ^= 0xFF;
        Assert.False(CryptoHelper.RsaVerify("payload", Convert.ToBase64String(bytes), publicKey));
    }
}
