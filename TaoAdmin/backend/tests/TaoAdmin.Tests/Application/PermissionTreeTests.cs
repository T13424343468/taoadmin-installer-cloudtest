using TaoAdmin.Application.Services;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>菜单树构建（纯逻辑，不触库）</summary>
public class PermissionTreeTests
{
    private static SysMenu Menu(long id, long parentId, string name, int order = 0, int type = 1)
        => new() { Id = id, ParentId = parentId, MenuName = name, OrderNum = order, MenuType = type };

    private readonly PermissionAppService _service = new(null!);

    [Fact]
    public void BuildTree_ShouldNestChildrenUnderParents()
    {
        var menus = new List<SysMenu>
        {
            Menu(1, 0, "系统管理", type: 0),
            Menu(2, 1, "用户管理"),
            Menu(3, 2, "新增用户", type: 2)
        };

        var tree = _service.BuildTree(menus);
        var root = Assert.Single(tree);
        Assert.Equal("1", root.id);
        Assert.Null(root.parentId);
        var child = Assert.Single(root.children);
        Assert.Equal("2", child.id);
        Assert.Equal("3", Assert.Single(child.children).id);
    }

    [Fact]
    public void BuildTree_ButtonType_ShouldMapToButton()
    {
        var tree = _service.BuildTree(new List<SysMenu> { Menu(1, 0, "b", type: 2) });
        Assert.Equal("button", Assert.Single(tree).type);
    }

    [Fact]
    public void BuildTree_DirType_ShouldMapToDir()
    {
        var tree = _service.BuildTree(new List<SysMenu> { Menu(1, 0, "d", type: 0) });
        Assert.Equal("dir", Assert.Single(tree).type);
    }

    [Fact]
    public void BuildTree_Siblings_ShouldSortByOrderNum()
    {
        var tree = _service.BuildTree(new List<SysMenu>
        {
            Menu(1, 0, "root", type: 0),
            Menu(2, 1, "second", order: 2),
            Menu(3, 1, "first", order: 1)
        });
        var root = Assert.Single(tree);
        Assert.Equal(new[] { "3", "2" }, root.children.Select(c => c.id));
    }

    [Fact]
    public void BuildTree_MissingParent_ShouldPromoteToRoot()
    {
        // 父节点不在集合中的菜单（如被过滤掉）会提升为根节点而不是丢失
        var tree = _service.BuildTree(new List<SysMenu>
        {
            Menu(1, 0, "root"),
            Menu(2, 99, "lost-parent")
        });
        Assert.Equal(new[] { "1", "2" }, tree.Select(n => n.id).OrderBy(x => x));
    }

    [Fact]
    public void BuildTree_EmptyInput_ShouldReturnEmpty()
        => Assert.Empty(_service.BuildTree(new List<SysMenu>()));
}
