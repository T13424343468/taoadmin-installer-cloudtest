using TaoAdmin.Deployer.Core;
using Xunit;

namespace Deployer.Core.Tests;

public class DeployPathsTests
{
    [Fact]
    public void FromExecutable_ResolvesStandardLayout()
    {
        // <root>/deploy/windows/TaoAdminDeployer.exe
        var exe = OperatingSystem.IsWindows()
            ? @"C:\apps\TaoAdmin\deploy\windows\TaoAdminDeployer.exe"
            : "/apps/TaoAdmin/deploy/windows/TaoAdminDeployer.exe";
        var paths = DeployPaths.FromExecutable(exe);

        var root = DeployPaths.ParentDir(DeployPaths.ParentDir(paths.WinDir));
        Assert.Equal(DeployPaths.Join(root, "deploy", "windows"), paths.WinDir);
        Assert.Equal(DeployPaths.Join(paths.WinDir, "runtime"), paths.RuntimeDir);
        Assert.Equal(DeployPaths.Join(paths.RuntimeDir, "pids"), paths.PidDir);
        Assert.Equal(DeployPaths.Join(paths.RuntimeDir, "logs"), paths.LogDir);
        Assert.Equal(DeployPaths.Join(paths.RuntimeDir, "downloads"), paths.DlDir);
        Assert.Equal(DeployPaths.Join(root, "deploy", "publish"), paths.PublishDir);
        Assert.Equal(DeployPaths.Join(paths.RuntimeDir, ".install-state"), paths.StateFile);
        Assert.Equal(DeployPaths.Join(root, "deploy", "sql", "tao_admin_init.sql"), paths.SqlFile);
        Assert.Equal(DeployPaths.Join(root, "deploy"), paths.ComposeDir);
        Assert.Equal(DeployPaths.Join(root, "frontend"), paths.FrontendDir);
    }

    [Fact]
    public void FromExecutable_WalksUpFromSubfolder()
    {
        // Published single-file in a subfolder still resolves.
        var exe = OperatingSystem.IsWindows()
            ? @"C:\apps\TaoAdmin\deploy\windows\sub\TaoAdminDeployer.exe"
            : "/apps/TaoAdmin/deploy/windows/sub/TaoAdminDeployer.exe";
        var paths = DeployPaths.FromExecutable(exe);
        Assert.EndsWith("windows", paths.WinDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParentDir_HandlesBothSeparators()
    {
        Assert.Equal(@"C:\apps", DeployPaths.ParentDir(@"C:\apps\TaoAdmin"));
        Assert.Equal("/apps", DeployPaths.ParentDir("/apps/TaoAdmin"));
        Assert.Equal(@"C:\apps", DeployPaths.ParentDir(@"C:\apps\TaoAdmin\"));
    }

    [Fact]
    public void Join_UsesPlatformSeparator()
    {
        var joined = DeployPaths.Join("base", "a", "b");
        Assert.Equal(Path.Combine("base", "a", "b"), joined);
    }

    [Fact]
    public void LeafName_ParsesBothSeparators()
    {
        Assert.Equal("windows", DeployPaths.LeafName(@"C:\apps\deploy\windows"));
        Assert.Equal("windows", DeployPaths.LeafName("/apps/deploy/windows"));
        Assert.Equal("windows", DeployPaths.LeafName("/apps/deploy/windows/"));
    }
}
