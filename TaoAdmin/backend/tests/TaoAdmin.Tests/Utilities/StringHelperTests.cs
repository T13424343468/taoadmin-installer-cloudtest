using TaoAdmin.Utilities;
using Xunit;

namespace TaoAdmin.Tests.Utilities;

/// <summary>字符串工具：掩码 / 脱敏 / 截断 / 命名转换</summary>
public class StringHelperTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\t\n", true)]
    [InlineData("x", false)]
    public void IsNullOrEmpty_ShouldTreatWhitespaceAsEmpty(string? value, bool expected)
        => Assert.Equal(expected, value.IsNullOrEmpty());

    [Theory]
    [InlineData("13812348000", "138****8000")]
    [InlineData("19999999999", "199****9999")]
    public void MaskPhone_ShouldMaskMiddleDigits(string phone, string expected)
        => Assert.Equal(expected, phone.MaskPhone());

    [Fact]
    public void MaskPhone_NonStandardLength_ShouldReturnOriginal()
        => Assert.Equal("12345", "12345".MaskPhone());

    [Theory]
    [InlineData("abcd@x.com", "a***@x.com")]
    [InlineData("zhangsan@qq.com", "z***@qq.com")]
    public void MaskEmail_ShouldKeepLocalFirstCharAndDomain(string email, string expected)
        => Assert.Equal(expected, email.MaskEmail());

    [Fact]
    public void MaskEmail_ShortLocalPart_ShouldReturnOriginal()
        => Assert.Equal("a@x.com", "a@x.com".MaskEmail());

    [Theory]
    [InlineData("张三丰", "张**")]
    [InlineData("AB", "A*")]
    [InlineData("X", "X")]
    public void MaskName_ShouldKeepFirstCharOnly(string name, string expected)
        => Assert.Equal(expected, name.MaskName());

    [Fact]
    public void Truncate_ShouldRespectMaxLength()
    {
        Assert.Equal("abc", "abc".Truncate(3));
        Assert.Equal("abcd…", "abcdefg".Truncate(5));
    }

    [Fact]
    public void Truncate_ShorterThanLimit_ShouldBeUnchanged()
        => Assert.Equal("hi", "hi".Truncate(10));

    [Theory]
    [InlineData("userName", "user_name")]
    [InlineData("UserName", "user_name")]
    [InlineData("HTTPCode", "h_t_t_p_code")]
    [InlineData("a", "a")]
    public void ToSnakeCase_ShouldConvert(string input, string expected)
        => Assert.Equal(expected, input.ToSnakeCase());

    [Theory]
    [InlineData("user_name", "UserName")]
    [InlineData("sys_user_role", "SysUserRole")]
    public void ToPascalCase_ShouldConvert(string input, string expected)
        => Assert.Equal(expected, input.ToPascalCase());

    [Fact]
    public void Repeat_ShouldRepeatValue()
        => Assert.Equal("ababab", "ab".Repeat(3));

    [Fact]
    public void RoundTrip_SnakeToPascalToSnake_ShouldStayConsistent()
        => Assert.Equal("order_id", "order_id".ToPascalCase().ToSnakeCase());
}
