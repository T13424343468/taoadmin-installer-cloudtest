using TaoAdmin.Utilities;
using Xunit;

namespace TaoAdmin.Tests.Utilities;

/// <summary>通用列表转树：菜单 / 部门结构（TKey 用可空引用类型承载"无父节点"语义）</summary>
public class TreeHelperTests
{
    private record Node(string Id, string Name, List<Node>? Children);

    private static List<Node> Build(List<(string Id, string? ParentId, string Name)> items)
        => TreeHelper.BuildTree<string, (string Id, string? ParentId, string Name), Node>(
            items,
            i => i.Id,
            i => i.ParentId,
            (i, children) => new Node(i.Id, i.Name, children));

    [Fact]
    public void SingleRoot_ShouldBuildWithoutChildren()
    {
        var tree = Build(new List<(string, string?, string)> { ("1", null, "root") });
        var root = Assert.Single(tree);
        Assert.Equal("1", root.Id);
        Assert.Null(root.Children);
    }

    [Fact]
    public void NestedThreeLevels_ShouldAttachCorrectly()
    {
        var tree = Build(new List<(string, string?, string)>
        {
            ("1", null, "root"),
            ("2", "1", "child"),
            ("3", "2", "grandchild")
        });

        var root = Assert.Single(tree);
        var child = Assert.Single(root.Children!);
        Assert.Equal("2", child.Id);
        Assert.Equal("3", Assert.Single(child.Children!).Id);
    }

    [Fact]
    public void MultipleRoots_ShouldBothBeListed()
    {
        var tree = Build(new List<(string, string?, string)>
        {
            ("1", null, "a"),
            ("2", null, "b"),
            ("3", "1", "c"),
            ("4", "2", "d")
        });
        Assert.Equal(new[] { "1", "2" }, tree.Select(n => n.Id));
    }

    [Fact]
    public void OrphanParent_ShouldNotThrow_AndBeDropped()
    {
        // 父节点缺失：节点进入 childrenMap 但无宿主，静默丢弃（算法约定，见疑点记录）
        var tree = Build(new List<(string, string?, string)>
        {
            ("1", null, "root"),
            ("2", "99", "orphan")
        });
        var root = Assert.Single(tree);
        Assert.Equal("1", root.Id);
        Assert.Null(root.Children);
    }

    [Fact]
    public void EmptyInput_ShouldReturnEmptyTree()
        => Assert.Empty(Build(new List<(string, string?, string)>()));

    [Fact]
    public void RealMenuShape_ZeroParentId_IsNotARoot()
    {
        // 复刻 SysMenu 场景：ParentId 为 long 且顶级为 0，包装成 string 后 "0" 不是 null，
        // 因此顶级菜单若传 0 而非 null 会被丢弃 —— 调用方必须显式传 null 表示根。
        var tree = Build(new List<(string, string?, string)>
        {
            ("1", null, "root"),
            ("2", "0", "zero-parent")
        });
        var root = Assert.Single(tree);
        Assert.Equal("1", root.Id);
        Assert.DoesNotContain(tree, n => n.Id == "2");
    }

    [Fact]
    public void DeepChain_ShouldNestRecursively()
    {
        var items = new List<(string, string?, string)>();
        for (var i = 1; i <= 50; i++)
            items.Add((i.ToString(), i == 1 ? null : (i - 1).ToString(), $"node{i}"));

        var tree = Build(items);
        var node = Assert.Single(tree);
        for (var depth = 2; depth <= 50; depth++)
            node = Assert.Single(node.Children!);
        Assert.Equal("50", node.Id);
    }
}
