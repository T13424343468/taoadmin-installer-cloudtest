using TaoAdmin.Deployer.Core;
using Xunit;

namespace Deployer.Core.Tests;

public class StateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _stateFile;

    public StateStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "taoadmin-deployer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _stateFile = Path.Combine(_dir, ".install-state");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void EnsureStateFile_CreatesEmptyFile()
    {
        var store = new StateStore(_stateFile);
        store.EnsureStateFile();
        Assert.True(File.Exists(_stateFile));
        Assert.Empty(store.Load());
    }

    [Fact]
    public void MarkDone_PersistsAcrossInstances()
    {
        var store = new StateStore(_stateFile);
        store.EnsureStateFile();
        store.MarkDone("dotnet-sdk");
        store.MarkDone("node");

        var store2 = new StateStore(_stateFile);
        var loaded = store2.Load();
        Assert.Contains("dotnet-sdk", loaded);
        Assert.Contains("node", loaded);
        Assert.DoesNotContain("mysql", loaded);
    }

    [Fact]
    public void MarkDone_IsIdempotent()
    {
        var store = new StateStore(_stateFile);
        store.EnsureStateFile();
        store.MarkDone("dotnet-sdk");
        store.MarkDone("dotnet-sdk");
        store.MarkDone("dotnet-sdk");
        Assert.Single(store.Load());
    }

    [Fact]
    public void MarkDone_WithoutStateFile_IsNoOp()
    {
        var store = new StateStore(_stateFile);
        store.MarkDone("dotnet-sdk"); // must not throw
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Clear_RemovesAllState()
    {
        var store = new StateStore(_stateFile);
        store.EnsureStateFile();
        store.MarkDone("dotnet-sdk");
        store.Clear();
        Assert.Empty(store.Load());
    }
}
