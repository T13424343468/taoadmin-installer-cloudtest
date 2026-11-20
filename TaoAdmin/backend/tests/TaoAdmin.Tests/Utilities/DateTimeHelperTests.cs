using TaoAdmin.Utilities;
using Xunit;

namespace TaoAdmin.Tests.Utilities;

/// <summary>日期工具：时间戳往返、周起始、相对时间、东八区</summary>
public class DateTimeHelperTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(1700000000L)]
    [InlineData(4102444800L)] // 2100-01-01
    public void UnixSeconds_Roundtrip_ShouldBeStable(long seconds)
    {
        var time = DateTimeHelper.FromUnixSeconds(seconds);
        Assert.Equal(seconds, time.ToUnixSeconds());
    }

    [Fact]
    public void ToUnixMilliseconds_ShouldBe1000xSeconds()
    {
        const long seconds = 1700000000;
        var ms = DateTimeHelper.FromUnixSeconds(seconds).ToUnixMilliseconds();
        Assert.Equal(seconds * 1000, ms);
    }

    [Fact]
    public void NowCn_ShouldBeAboutUtcPlus8()
    {
        var delta = DateTimeHelper.NowCn - DateTime.UtcNow;
        Assert.InRange(delta.TotalHours, 7.99, 8.01);
    }

    [Fact]
    public void StartOfWeek_ShouldReturnMonday()
    {
        var wed = new DateTime(2026, 11, 18); // 周三
        Assert.Equal(new DateTime(2026, 11, 16), wed.StartOfWeek()); // 周一
        Assert.Equal(DayOfWeek.Monday, wed.StartOfWeek().DayOfWeek);
    }

    [Fact]
    public void StartOfWeek_Sunday_ShouldBelongToPreviousWeek()
    {
        var sun = new DateTime(2026, 11, 22); // 周日
        Assert.Equal(new DateTime(2026, 11, 16), sun.StartOfWeek());
    }

    [Fact]
    public void StartAndEndOfDay_ShouldCoverWholeDay()
    {
        var t = new DateTime(2026, 11, 18, 10, 30, 0);
        Assert.Equal(new DateTime(2026, 11, 18, 0, 0, 0), t.StartOfDay());
        Assert.Equal(t.Date.AddDays(1).AddTicks(-1), t.EndOfDay());
        Assert.True(t.StartOfDay() <= t && t <= t.EndOfDay());
    }

    [Fact]
    public void ToRelativeText_ShouldDescribeRecentTimes()
    {
        Assert.Equal("刚刚", DateTime.Now.AddSeconds(-30).ToRelativeText());
        Assert.Equal("5 分钟前", DateTime.Now.AddMinutes(-5).ToRelativeText());
        Assert.Equal("3 小时前", DateTime.Now.AddHours(-3).ToRelativeText());
        Assert.StartsWith("昨天 ", DateTime.Now.AddHours(-30).ToRelativeText());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", DateTime.Now.AddDays(-10).ToRelativeText());
    }
}
