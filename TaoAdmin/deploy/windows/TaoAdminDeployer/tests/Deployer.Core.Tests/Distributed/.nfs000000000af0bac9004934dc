using TaoAdmin.Deployer.Core.Distributed;
using Xunit;

namespace Deployer.Core.Tests.Distributed;

public class ClusterModelsTests
{
    [Fact]
    public void MiddlewareFlag_MapsByKind()
    {
        Assert.True(new ServiceRole { Kind = ServiceKind.Database }.IsMiddleware);
        Assert.True(new ServiceRole { Kind = ServiceKind.Cache }.IsMiddleware);
        Assert.False(new ServiceRole { Kind = ServiceKind.Gateway }.IsMiddleware);
        Assert.False(new ServiceRole { Kind = ServiceKind.AppService }.IsMiddleware);
    }

    [Fact]
    public void Node_Defaults_AreSane()
    {
        var n = new ClusterNode();
        Assert.Equal(NodeState.Pending, n.State);
        Assert.Equal(NodeAccess.Ssh, n.Access);
        Assert.Equal(22, n.SshPort);
        Assert.Equal(5400, n.AgentPort);
    }
}
