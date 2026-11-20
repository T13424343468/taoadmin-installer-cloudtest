using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace TaoAdmin.Common.Security;

/// <summary>
/// 图形验证码：SVG 输出（无第三方依赖），答案存缓存一次性消费（校验即删，过期/错误同删）。
/// </summary>
public class CaptchaService(IMemoryCache cache)
{
    /// <summary>答案有效期（分钟）</summary>
    public const int TtlMinutes = 5;

    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 去 0O1lI

    /// <summary>生成验证码：返回 (id, svg)。</summary>
    public (string Id, string Svg) Generate()
    {
        var chars = new char[4];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = Chars[RandomNumberGenerator.GetInt32(Chars.Length)];
        var code = new string(chars);
        var id = Guid.NewGuid().ToString("N");
        cache.Set(CacheKey(id), code, TimeSpan.FromMinutes(TtlMinutes));
        return (id, Render(code));
    }

    /// <summary>校验并消费：正确返回 true（一次性，无论对错答案即失效）。</summary>
    public bool Verify(string? id, string? input)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(input)) return false;
        var key = CacheKey(id);
        if (!cache.TryGetValue(key, out string? answer) || answer is null) return false;
        cache.Remove(key);
        return string.Equals(answer, input.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>测试用：直接取答案（仅 id 有效时）。</summary>
    public string? Peek(string id) => cache.TryGetValue(CacheKey(id), out string? v) ? v : null;

    private static string CacheKey(string id) => $"captcha:{id}";

    private static string Render(string code)
    {
        var rand = new Random(code.GetHashCode());
        string Noise()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                int x1 = rand.Next(0, 160), y1 = rand.Next(0, 44), x2 = rand.Next(0, 160), y2 = rand.Next(0, 44);
                sb.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='#{rand.Next(0x1000000):x6}' stroke-width='1' opacity='0.5'/>");
            }
            return sb.ToString();
        }
        var chars = new System.Text.StringBuilder();
        for (int i = 0; i < code.Length; i++)
        {
            int x = 14 + i * 34 + rand.Next(-2, 3);
            int y = 30 + rand.Next(-4, 5);
            int rot = rand.Next(-20, 21);
            chars.Append($"<text x='{x}' y='{y}' font-size='26' font-family='monospace' font-weight='bold' fill='#{rand.Next(0x1000000):x6:x6}' transform='rotate({rot} {x} {y})'>{code[i]}</text>");
        }
        return $"<svg xmlns='http://www.w3.org/2000/svg' width='160' height='44' viewBox='0 0 160 44'><rect width='160' height='44' fill='#f2f3f5'/>{Noise()}{chars}</svg>";
    }
}
