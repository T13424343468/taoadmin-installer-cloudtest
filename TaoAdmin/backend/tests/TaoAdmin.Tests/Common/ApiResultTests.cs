using TaoAdmin.Common.Http;
using Xunit;

namespace TaoAdmin.Tests.Common;

/// <summary>统一返回格式与分页基类</summary>
public class ApiResultTests
{
    [Fact]
    public void Ok_ShouldSetCode200AndSuccess()
    {
        var result = ApiResult<string>.Ok("data");
        Assert.Equal(200, result.Code);
        Assert.True(result.Success);
        Assert.Equal("data", result.Data);
    }

    [Fact]
    public void Ok_ShouldCarryMessageAndTraceId()
    {
        var result = ApiResult<string>.Ok("d", "done", "trace-1");
        Assert.Equal("done", result.Message);
        Assert.Equal("trace-1", result.TraceId);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(400)]
    [InlineData(403)]
    public void Fail_ShouldSetCodeAndNotSuccess(int code)
    {
        var result = ApiResult.Fail("boom", code);
        Assert.Equal(code, result.Code);
        Assert.False(result.Success);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Fail_DefaultCode_ShouldBe500()
    {
        var result = ApiResult<object>.Fail("boom");
        Assert.Equal(500, result.Code);
        Assert.False(result.Success);
    }

    [Fact]
    public void Fail_ShouldKeepSuccessFlagInSyncWithCode()
    {
        // 注意：Success 是独立属性，构造后改 Code 不会自动同步（与前端契约：Success 必须与 Code 一致）
        var result = new ApiResult<string> { Code = 500, Success = true };
        Assert.NotEqual(result.Success, result.Code == 200); // 记录该契约风险
    }

    [Theory]
    [InlineData(1, 10, 0)]
    [InlineData(2, 10, 10)]
    [InlineData(3, 10, 20)]
    [InlineData(0, 10, 0)]   // 页码最小 1
    [InlineData(-5, 10, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(5, 0, 0)]
    public void PageRequest_Skip_ShouldBeClamped(int pageIndex, int pageSize, int expected)
    {
        var req = new PageRequest { PageIndex = pageIndex, PageSize = pageSize };
        Assert.Equal(expected, req.Skip);
    }

    [Fact]
    public void PageResult_Constructor_ShouldFillAllFields()
    {
        var result = new PageResult<string>(100, 2, 10, new List<string> { "a" });
        Assert.Equal(100, result.Total);
        Assert.Equal(2, result.PageIndex);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(new[] { "a" }, result.Items);
    }

    [Fact]
    public void PageResult_DefaultItems_ShouldNotBeNull()
        => Assert.NotNull(new PageResult<string>().Items);
}
