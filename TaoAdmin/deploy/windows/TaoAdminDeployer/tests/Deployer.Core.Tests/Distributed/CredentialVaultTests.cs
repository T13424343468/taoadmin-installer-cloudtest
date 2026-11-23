using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class CredentialVaultTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vault-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Store_Retrieve_RoundTrips()
    {
        var vault = new CredentialVault(_dir);
        vault.Store("n1", "S3cret-密码");
        Assert.Equal("S3cret-密码", vault.Retrieve("n1"));
    }

    [Fact]
    public void StoredFile_IsNotPlaintext()
    {
        var vault = new CredentialVault(_dir);
        vault.Store("n1", "MyPlainPassword123");
        var files = Directory.GetFiles(_dir, "*.cred");
        var blob = File.ReadAllBytes(files[0]);
        Assert.DoesNotContain("MyPlainPassword123", System.Text.Encoding.UTF8.GetString(blob));
        Assert.Equal("TA20", System.Text.Encoding.ASCII.GetString(blob[..4]));
    }

    [Fact]
    public void Retrieve_Missing_ReturnsNull()
    {
        var vault = new CredentialVault(_dir);
        Assert.Null(vault.Retrieve("ghost"));
        Assert.Null(vault.Retrieve(""));
    }

    [Fact]
    public void Delete_Removes_AndReturnsFalseOnSecond()
    {
        var vault = new CredentialVault(_dir);
        vault.Store("n1", "x");
        Assert.True(vault.Delete("n1"));
        Assert.False(vault.Delete("n1"));
        Assert.Null(vault.Retrieve("n1"));
    }

    [Fact]
    public void Store_Empty_Throws()
    {
        var vault = new CredentialVault(_dir);
        Assert.Throws<ArgumentException>(() => vault.Store("", "x"));
        Assert.Throws<ArgumentException>(() => vault.Store("n1", ""));
    }

    [Fact]
    public void CorruptedFile_Throws()
    {
        var vault = new CredentialVault(_dir);
        vault.Store("n1", "x");
        File.WriteAllBytes(Path.Combine(_dir, "n1.cred"), new byte[] { 1, 2, 3 });
        Assert.Throws<InvalidOperationException>(() => vault.Retrieve("n1"));
    }

    [Fact]
    public void KeyPersists_AcrossInstances()
    {
        var v1 = new CredentialVault(_dir);
        v1.Store("n1", "secret");
        var v2 = new CredentialVault(_dir); // 复用同一 vault.key
        Assert.Equal("secret", v2.Retrieve("n1"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
