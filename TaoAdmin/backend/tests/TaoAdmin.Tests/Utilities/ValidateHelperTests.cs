using TaoAdmin.Utilities;
using Xunit;

namespace TaoAdmin.Tests.Utilities;

/// <summary>密码强度 / 手机号 / 邮箱 / 用户名 / Cron 校验</summary>
public class ValidateHelperTests
{
    [Theory]
    [InlineData("Abc12345", true)]      // 大写+小写+数字，三类
    [InlineData("Abcdefg!", true)]      // 大写+小写+特殊，三类
    [InlineData("abcdefgh", false)]     // 仅小写
    [InlineData("ABCDEFGH", false)]     // 仅大写
    [InlineData("12345678", false)]     // 仅数字
    [InlineData("abc12", false)]        // 不足 8 位
    [InlineData("Aa1!Aa", false)]       // 边界：仅 7 位，即便四类齐全也不达标
    [InlineData("Aa1!Aa1!", true)]      // 边界：恰好 8 位
    [InlineData("", false)]
    public void IsStrongPassword_ShouldRequireThreeKinds(string password, bool expected)
        => Assert.Equal(expected, ValidateHelper.IsStrongPassword(password));

    [Fact]
    public void IsStrongPassword_DigitsAndSpecialOnly_ShouldFail()
        => Assert.False(ValidateHelper.IsStrongPassword("12345678!"));

    [Theory]
    [InlineData("13812340000", true)]
    [InlineData("19912340000", true)]
    [InlineData("12345678901", false)]   // 第二位必须 3-9
    [InlineData("1381234000", false)]    // 10 位
    [InlineData("138123400001", false)]  // 12 位
    [InlineData("abc12340000", false)]
    [InlineData("", false)]
    public void IsPhone_ShouldMatchMainlandMobileRule(string value, bool expected)
        => Assert.Equal(expected, ValidateHelper.IsPhone(value));

    [Theory]
    [InlineData("a@b.cn", true)]
    [InlineData("first.last+tag@sub.domain.com", true)]
    [InlineData("a@b", false)]          // 缺少顶级域
    [InlineData("@b.com", false)]
    [InlineData("a@@b.com", false)]
    [InlineData("", false)]
    public void IsEmail_ShouldValidateFormat(string value, bool expected)
        => Assert.Equal(expected, ValidateHelper.IsEmail(value));

    [Theory]
    [InlineData("tao01", true)]
    [InlineData("Admin_2026", true)]
    [InlineData("ab", false)]            // 至少 3 位
    [InlineData("1abc", false)]          // 必须字母开头
    [InlineData("a very long username over 20", false)]
    [InlineData("bad-name", false)]      // 连字符不允许
    public void IsUserName_ShouldValidateFormat(string value, bool expected)
        => Assert.Equal(expected, ValidateHelper.IsUserName(value));

    [Theory]
    [InlineData("0 0 * * * ?", true)]       // 6 段 Quartz
    [InlineData("0 0 12 * * ?", true)]
    [InlineData("0 0 12 1/5 * ?", true)]
    [InlineData("0 0 12 * * ? 2026", true)] // 7 段（含年份）
    [InlineData("* * * * *", false)]        // 5 段（Unix cron）不支持
    [InlineData("not a cron", false)]
    [InlineData("", false)]
    public void IsValidCron_ShouldRequireSixOrSevenFields(string cron, bool expected)
        => Assert.Equal(expected, ValidateHelper.IsValidCron(cron));
}
