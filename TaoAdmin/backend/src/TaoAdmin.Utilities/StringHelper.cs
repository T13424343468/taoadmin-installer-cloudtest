using System.Security.Cryptography;
using System.Text;

namespace TaoAdmin.Utilities;

/// <summary>
/// 字符串工具：判空、掩码、脱敏、编码等常用操作。
/// </summary>
public static class StringHelper
{
    public static bool IsNullOrEmpty(this string? value) => string.IsNullOrWhiteSpace(value);

    public static bool NotEmpty(this string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>截断超长文本，超出部分以 … 结尾</summary>
    public static string Truncate(this string value, int maxLength)
        => value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    /// <summary>手机号掩码：138****8000</summary>
    public static string MaskPhone(this string phone)
        => phone.Length == 11 ? $"{phone[..3]}****{phone[7..]}" : phone;

    /// <summary>邮箱掩码：a***b@x.com</summary>
    public static string MaskEmail(this string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : $"{email[0]}***{email[at..]}";
    }

    /// <summary>姓名掩码：保留首字，其余 *</summary>
    public static string MaskName(this string name)
        => name.Length <= 1 ? name : name[0] + new string('*', name.Length - 1);

    /// <summary>camelCase / PascalCase 转 snake_case</summary>
    public static string ToSnakeCase(this string value)
        => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToLower();

    /// <summary>snake_case 转 PascalCase</summary>
    public static string ToPascalCase(this string value)
        => string.Concat(value.Split('_').Select(s => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..]));

    /// <summary>重复字符串（net8 无 string.Repeat）</summary>
    public static string Repeat(this string value, int count)
        => string.Concat(Enumerable.Repeat(value, count));

    public static byte[] ToBytes(this string value) => Encoding.UTF8.GetBytes(value);
}
