using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TaoAdmin.Common.Auth;

/// <summary>JWT 配置项（对应 appsettings.json 的 Jwt 节，环境变量 Jwt__SecretKey 等可覆盖）</summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "TaoAdmin";
    public string Audience { get; set; } = "TaoAdmin";
    /// <summary>签名密钥，生产环境务必通过环境变量/配置中心注入，长度不少于 32 字符</summary>
    public string SecretKey { get; set; } = "TaoAdmin@2024#SecretKey*MustBeLongEnough!";
    /// <summary>访问令牌有效期（分钟）</summary>
    public int ExpireMinutes { get; set; } = 120;
    /// <summary>刷新令牌有效期（天）</summary>
    public int RefreshExpireDays { get; set; } = 7;
}

/// <summary>
/// JWT 令牌服务：生成访问令牌 / 刷新令牌，校验令牌。
/// Claims 约定：UserId、Name（用户名）、Role（角色码，多个）、perm（权限码，多个）。
/// </summary>
public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options)
    {
        _options = options;
    }

    /// <summary>生成访问令牌（tenantId 写入 tenant_id 声明，供多租户过滤使用）</summary>
    public (string Token, DateTime ExpiresAt) GenerateToken(
        long userId, string userName, IEnumerable<string> roles, IEnumerable<string> permissions, long tenantId = 0)
    {
        var (token, expiresAt, _) = GenerateTokenWithSession(userId, userName, roles, permissions, tenantId);
        return (token, expiresAt);
    }

    /// <summary>生成访问令牌并返回会话标识（jti），供在线会话登记与强制下线使用</summary>
    public (string Token, DateTime ExpiresAt, string SessionId) GenerateTokenWithSession(
        long userId, string userName, IEnumerable<string> roles, IEnumerable<string> permissions, long tenantId = 0)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, sessionId),
            new("UserId", userId.ToString()),
            new(ClaimTypes.Name, userName),
            new("tenant_id", tenantId.ToString())
        };
        // 角色：多个角色写入多个 Role 声明
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        // 权限码：多个权限写入多个 perm 声明，供 PermissionAuthorizationHandler 校验
        claims.AddRange(permissions.Distinct().Select(p => new Claim("perm", p)));

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpireMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt, sessionId);
    }

    /// <summary>生成刷新令牌（随机 256 位，存 Redis）</summary>
    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>校验令牌，非法或过期返回 null</summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _options.Issuer,
                ValidateAudience = true,
                ValidAudience = _options.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>刷新令牌有效期（分钟）</summary>
    public int ExpireMinutes => _options.ExpireMinutes;

    /// <summary>刷新令牌有效期（天）</summary>
    public int RefreshExpireDays => _options.RefreshExpireDays;
}
