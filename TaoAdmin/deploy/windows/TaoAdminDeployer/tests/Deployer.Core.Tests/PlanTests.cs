using TaoAdmin.Deployer.Core;
using Xunit;

namespace Deployer.Core.Tests;

public class PlanTests
{
    [Fact]
    public void NativePlan_HasExpectedStepOrder()
    {
        var plan = DeployEngine.NativePlan(new DeployOptions());
        var ids = plan.Select(s => s.Id).ToArray();
        Assert.Equal(
            new[] { "env-check", "dotnet-sdk", "node", "mysql", "redis", "backend", "frontend", "db-init", "start", "health" },
            ids);
    }

    [Fact]
    public void DockerPlan_HasExpectedStepOrder()
    {
        var plan = DeployEngine.DockerPlan(new DeployOptions { Route = "docker" });
        var ids = plan.Select(s => s.Id).ToArray();
        Assert.Equal(
            new[] { "env-check", "docker-install", "docker-mode", "compose-up", "docker-health" },
            ids);
    }

    [Fact]
    public void Plans_HavePositiveWeights()
    {
        foreach (var s in DeployEngine.NativePlan(new DeployOptions()).Concat(DeployEngine.DockerPlan(new DeployOptions())))
        {
            Assert.True(s.Weight > 0, $"step {s.Id} weight must be positive");
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
        }
    }

    [Fact]
    public void PlanStepIds_AreUnique()
    {
        foreach (var plan in new[] { DeployEngine.NativePlan(new DeployOptions()), DeployEngine.DockerPlan(new DeployOptions()) })
            Assert.Equal(plan.Count, plan.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void DownloadUrls_AreHttps()
    {
        Assert.StartsWith("https://", DeployOptions.Urls.DotnetInstall);
        Assert.StartsWith("https://", DeployOptions.Urls.NodeZip);
        Assert.StartsWith("https://", DeployOptions.Urls.MysqlZip);
        Assert.StartsWith("https://", DeployOptions.Urls.RedisZip);
        Assert.StartsWith("https://", DeployOptions.Urls.DockerSetup);
        // Node version must satisfy rolldown engines (pitfall #55).
        Assert.Contains("v22.", DeployOptions.NodeVersion);
    }
}
