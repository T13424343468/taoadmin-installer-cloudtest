using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class NativeRouteRemovalTests
{
    [Theory]
    [InlineData("native")]
    [InlineData("docker")]
    [InlineData("Docker")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void NormalizeRoute_AlwaysDocker(string? route)
    {
        Assert.Equal("docker", NativeRouteRemoval.NormalizeRoute(route));
    }
}
