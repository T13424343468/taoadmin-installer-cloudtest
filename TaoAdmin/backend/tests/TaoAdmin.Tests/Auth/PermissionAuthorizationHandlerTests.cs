using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using TaoAdmin.Common.Auth;
using Xunit;

namespace TaoAdmin.Tests.Auth;

/// <summary>权限码授权处理器：命中放行、超管放行、未认证拒绝、无权限拒绝</summary>
public class PermissionAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext NewContext(ClaimsPrincipal user, string required)
        => new(new[] { new PermissionRequirement(required) }, user, null);

    private static async Task<bool> AllowedAsync(ClaimsPrincipal user, string required)
    {
        var handler = new PermissionAuthorizationHandler();
        var ctx = NewContext(user, required);
        await handler.HandleAsync(ctx);
        return ctx.HasSucceeded;
    }

    private static ClaimsPrincipal User(params string[] perms)
    {
        var identity = new ClaimsIdentity(perms.Select(p => new Claim("perm", p)), "Bearer");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task MatchingPermission_ShouldSucceed()
    {
        var user = User("sys:user:add", "sys:user:list");
        Assert.True(await AllowedAsync(user, "sys:user:add"));
    }

    [Fact]
    public async Task PermissionMatch_ShouldBeCaseInsensitive()
    {
        var user = User("sys:user:add");
        Assert.True(await AllowedAsync(user, "SYS:USER:ADD"));
    }

    [Fact]
    public async Task MissingPermission_ShouldNotSucceed()
    {
        var user = User("sys:user:list");
        Assert.False(await AllowedAsync(user, "sys:user:add"));
    }

    [Fact]
    public async Task NoPermClaims_ShouldNotSucceed()
    {
        var user = User();
        Assert.False(await AllowedAsync(user, "sys:user:add"));
    }

    [Fact]
    public async Task SuperAdminRole_ShouldBypassPermissionCheck()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "admin") }, "Bearer");
        var superAdmin = new ClaimsPrincipal(identity);
        Assert.True(await AllowedAsync(superAdmin, "anything:at:all"));
    }

    [Fact]
    public async Task UnauthenticatedUser_ShouldNotSucceed()
    {
        // 无认证类型的 Identity = 未认证
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.False(await AllowedAsync(anonymous, "sys:user:add"));
    }

    [Fact]
    public void PermissionAttribute_PolicyName_ShouldFollowConvention()
    {
        var attr = new PermissionAttribute("sys:user:add");
        Assert.Equal("PERM:sys:user:add", attr.Policy);
        Assert.Equal("sys:user:add", attr.PermsCode);
    }
}
