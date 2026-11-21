using TaoAdmin.Common.Http;
using TaoAdmin.Domain;
using Xunit;

namespace TaoAdmin.Tests.Common;

/// <summary>分页参数与分页结果：页码边界（第 0/负页码按第 1 页处理）</summary>
public class PageRequestTests
{
    [Theory]
    [InlineData(1, 10, 0)]     // 第 1 页从 0 跳过
    [InlineData(3, 20, 40)]    // 常规跳过
    [InlineData(0, 10, 0)]     // 页码 0 视为第 1 页，不产生负跳过
    [InlineData(-5, 10, 0)]    // 负页码同理
    public void Skip_ShouldClampToNonNegative(int pageIndex, int pageSize, int expected)
        => Assert.Equal(expected, new PageRequest { PageIndex = pageIndex, PageSize = pageSize }.Skip);

    [Fact]
    public void Defaults_ShouldBePage1Size10()
    {
        var req = new PageRequest();
        Assert.Equal(1, req.PageIndex);
        Assert.Equal(10, req.PageSize);
    }
}

/// <summary>分页结果：构造与空态</summary>
public class PageResultTests
{
    [Fact]
    public void Constructor_ShouldFillAllFields()
    {
        var items = new List<string> { "a", "b" };
        var page = new PageResult<string>(total: 12, pageIndex: 2, pageSize: 2, items);

        Assert.Equal(12, page.Total);
        Assert.Equal(2, page.PageIndex);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(items, page.Items);
    }

    [Fact]
    public void EmptyResult_ItemsShouldDefaultToEmptyList()
    {
        var page = new PageResult<string>();
        Assert.NotNull(page.Items);
        Assert.Empty(page.Items);
    }
}

/// <summary>领域异常：消息与业务错误码</summary>
public class DomainExceptionTests
{
    [Fact]
    public void DefaultCode_ShouldBe400()
    {
        var ex = new DomainException("参数错误");
        Assert.Equal("参数错误", ex.Message);
        Assert.Equal(400, ex.Code);
    }

    [Fact]
    public void CustomCode_ShouldBePreserved()
    {
        var ex = new DomainException("无权限", 403);
        Assert.Equal(403, ex.Code);
    }
}
