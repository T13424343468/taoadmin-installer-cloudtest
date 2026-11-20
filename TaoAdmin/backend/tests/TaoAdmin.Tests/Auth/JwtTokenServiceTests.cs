using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.MultiTenancy;
using Xunit;

namespace TaoAdmin.Tests.Auth;

/// <summary>JWT 令牌：签发 / 校验 / 防篡改</summary>
public class JwtTokenServiceTests
{
    private static JwtOptions Options(int expireMinutes = 120) => new()
    {
        Issuer = "TaoAdmin",
        Audience = "TaoAdmin",
        SecretKey = "unit-test-secret-key-must-be-32+chars!!",
        ExpireMinutes = expireMinutes
    };

    private static ClaimsPrincipal Validate(JwtOptions options, string token)
        => new JwtTokenService(options).ValidateToken(token)!;

    [Fact]
    public void GenerateToken_Roundtrip_ShouldKeepIdentityAndTenant()
    {
        var svc = new JwtTokenService(Options());
        var (token, _) = svc.GenerateToken(9527, "tao", new[] { "admin" }, new[] { "sys:user:add" }, tenantId: 66);

        var principal = Validate(Options(), token);
        Assert.NotNull(principal);
        Assert.Equal("9527", principal!.FindFirst("UserId")!.Value);
        Assert.Equal("66", principal.FindFirst("tenant_id")!.Value);
        Assert.Equal("tao", principal.FindFirst(ClaimTypes.Name)!.Value);
        Assert.Equal(66, principal.GetTenantId());
    }

    [Fact]
    public void GenerateToken_MultipleRolesAndPerms_ShouldBePreserved()
    {
        var svc = new JwtTokenService(Options());
        var (token, _) = svc.GenerateToken(1, "u",
            new[] { "editor", "auditor" }, new[] { "sys:user:add", "sys:user:edit", "sys:user:edit" });

        var principal = Validate(Options(), token);
        Assert.Equal(new[] { "auditor", "editor" },
            principal!.FindAll(ClaimTypes.Role).Select(c => c.Value).OrderBy(x => x));
        // 重复权限码去重
        Assert.Equal(new[] { "sys:user:add", "sys:user:edit" },
            principal.FindAll("perm").Select(c => c.Value).OrderBy(x => x));
    }

    [Fact]
    public void ValidateToken_WrongSecretKey_ShouldReturnNull()
    {
        var svc = new JwtTokenService(Options());
        var (token, _) = svc.GenerateToken(1, "u", new[] { "r" }, Array.Empty<string>());

        var otherKey = Options();
        otherKey.SecretKey = "another-secret-key-also-long-enough!!";
        Assert.Null(new JwtTokenService(otherKey).ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_TamperedPayload_ShouldReturnNull()
    {
        var svc = new JwtTokenService(Options());
        var (token, _) = svc.GenerateToken(1, "u", new[] { "r" }, Array.Empty<string>());
        // 篡改 payload 中间一个字符
        var parts = token.Split('.');
        var payload = parts[1].ToCharArray();
        payload[0] = payload[0] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{new string(payload)}.{parts[2]}";

        Assert.Null(new JwtTokenService(Options()).ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_ExpiredToken_ShouldReturnNull()
    {
        // GenerateToken(ExpireMinutes=0) 会被 JwtSecurityToken 拒绝（expires 必须 > notBefore），
        // 因此这里手工构造一张已过期令牌，验证 ValidateToken 的过期拒绝路径
        var options = Options();
        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(options.SecretKey));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddMinutes(-5),
            signingCredentials: creds);

        Assert.Null(new JwtTokenService(options).ValidateToken(new JwtSecurityTokenHandler().WriteToken(token)));
    }

    [Fact]
    public void GenerateToken_ExpiresAt_ShouldMatchConfiguredWindow()
    {
        var svc = new JwtTokenService(Options(expireMinutes: 7));
        var (_, expiresAt) = svc.GenerateToken(1, "u", Array.Empty<string>(), Array.Empty<string>());
        Assert.Equal(7, (expiresAt - DateTime.UtcNow).TotalMinutes + 0.2, 0);
    }

    [Fact]
    public void GenerateRefreshToken_ShouldBeRandomAnd256Bit()
    {
        var svc = new JwtTokenService(Options());
        var seen = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            var t = svc.GenerateRefreshToken();
            Assert.Equal(44, t.Length); // 32 字节 Base64
            Assert.True(seen.Add(t));
        }
    }
}

/// <summary>TenantPrincipalExtensions：从令牌读租户（扩展方法在 TaoAdmin.Common.MultiTenancy）</summary>
public class TenantPrincipalFromJwtTests
{
    [Fact]
    public void GetTenantId_ShouldReadJwtTenantClaim()
    {
        var options = new JwtOptions { SecretKey = "unit-test-secret-key-must-be-32+chars!!" };
        var svc = new JwtTokenService(options);
        var (token, _) = svc.GenerateToken(1, "u", Array.Empty<string>(), Array.Empty<string>(), tenantId: 1234);

        // 走 JwtTokenService.ValidateToken 的完整校验路径（与生产一致）
        var principal = svc.ValidateToken(token);
        Assert.NotNull(principal);
        Assert.Equal(1234, principal!.GetTenantId());
    }
}
