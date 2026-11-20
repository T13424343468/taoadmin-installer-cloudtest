namespace TaoAdmin.Utilities;

/// <summary>
/// 日期时间工具：时间戳、格式化、相对时间等。
/// </summary>
public static class DateTimeHelper
{
    /// <summary>东八区</summary>
    public static readonly TimeZoneInfo CnTimeZone = TimeZoneInfo.CreateCustomTimeZone("CST+8", TimeSpan.FromHours(8), "中国标准时间", "中国标准时间");

    public const string FullFormat = "yyyy-MM-dd HH:mm:ss";
    public const string ShortFormat = "yyyy-MM-dd";

    public static string Format(this DateTime time, string format = FullFormat) => time.ToString(format);

    /// <summary>当前北京时间</summary>
    public static DateTime NowCn => TimeZoneInfo.ConvertTime(DateTime.UtcNow, CnTimeZone);

    /// <summary>Unix 秒级时间戳</summary>
    public static long ToUnixSeconds(this DateTime time)
        => new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeSeconds();

    /// <summary>Unix 毫秒级时间戳</summary>
    public static long ToUnixMilliseconds(this DateTime time)
        => new DateTimeOffset(time.ToUniversalTime()).ToUnixTimeMilliseconds();

    /// <summary>Unix 秒级时间戳 -> 时间</summary>
    public static DateTime FromUnixSeconds(long seconds)
        => DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().DateTime;

    /// <summary>相对时间：刚刚 / n 分钟前 / n 小时前 / 昨天 / 日期</summary>
    public static string ToRelativeText(this DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1) return "刚刚";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
        if (span.TotalDays < 2) return "昨天 " + time.ToString("HH:mm");
        return time.ToString(FullFormat);
    }

    /// <summary>本周一（周一为一周开始）</summary>
    public static DateTime StartOfWeek(this DateTime time)
    {
        var days = ((int)time.DayOfWeek + 6) % 7;
        return time.Date.AddDays(-days);
    }

    public static DateTime StartOfDay(this DateTime time) => time.Date;
    public static DateTime EndOfDay(this DateTime time) => time.Date.AddDays(1).AddTicks(-1);
}
