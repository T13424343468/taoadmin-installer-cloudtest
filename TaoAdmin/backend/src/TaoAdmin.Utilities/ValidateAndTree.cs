using System.Text.RegularExpressions;

namespace TaoAdmin.Utilities;

/// <summary>
/// 通用校验工具：手机号、邮箱、用户名、密码强度、Cron 等。
/// </summary>
public static partial class ValidateHelper
{
    [GeneratedRegex(@"^1[3-9]\d{9}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[\w.+-]+@[\w-]+(\.[\w-]+)+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[a-zA-Z][a-zA-Z0-9_]{2,19}$")]
    private static partial Regex UserNameRegex();

    public static bool IsPhone(string value) => PhoneRegex().IsMatch(value);
    public static bool IsEmail(string value) => EmailRegex().IsMatch(value);
    public static bool IsUserName(string value) => UserNameRegex().IsMatch(value);

    /// <summary>密码强度：至少 8 位，含大小写字母、数字、特殊字符中至少三类（内置默认口径，供无策略场景兜底）</summary>
    public static bool IsStrongPassword(string password)
    {
        if (password.Length < 8) return false;
        int kinds = 0;
        if (password.Any(char.IsLower)) kinds++;
        if (password.Any(char.IsUpper)) kinds++;
        if (password.Any(char.IsDigit)) kinds++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) kinds++;
        return kinds >= 3;
    }

    /// <summary>密码字符类别数：小写/大写/数字/特殊字符四类中命中的类数</summary>
    public static int PasswordKindCount(string password)
    {
        int kinds = 0;
        if (password.Any(char.IsLower)) kinds++;
        if (password.Any(char.IsUpper)) kinds++;
        if (password.Any(char.IsDigit)) kinds++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) kinds++;
        return kinds;
    }

    /// <summary>
    /// 按可配置策略校验密码（纯函数，一处文案三处引用）。
    /// 通过返回 null；不通过返回明确原因文案（长度/类数），供改密、建人、导入同口径拒收。
    /// </summary>
    public static string? CheckPassword(string? password, int minLength, int minKinds)
    {
        var pwd = password ?? string.Empty;
        if (pwd.Length < minLength)
            return $"密码长度不能少于 {minLength} 位";
        if (PasswordKindCount(pwd) < minKinds)
            return $"密码须包含大小写字母、数字、特殊字符中至少 {minKinds} 类";
        return null;
    }

    /// <summary>校验 Cron 表达式（6 段 Quartz 格式）</summary>
    public static bool IsValidCron(string cron)
    {
        var parts = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 6 or 7;
    }
}

/// <summary>
/// 通用列表转树工具：菜单、部门等 parentId 结构复用。
/// </summary>
public static class TreeHelper
{
    /// <summary>把扁平列表转成树。idSelector/parentSelector 取主键与父键，buildNode 输出节点。</summary>
    public static List<TNode> BuildTree<TKey, TItem, TNode>(
        IEnumerable<TItem> items,
        Func<TItem, TKey> idSelector,
        Func<TItem, TKey?> parentSelector,
        Func<TItem, List<TNode>?, TNode> buildNode)
        where TKey : notnull
    {
        var itemList = items.ToList();
        var childrenMap = itemList
            .Where(i => parentSelector(i) != null)
            .GroupBy(i => parentSelector(i)!)
            .ToDictionary(g => g.Key, g => g.ToList());
        var roots = itemList.Where(i => parentSelector(i) == null).ToList();

        List<TNode> Build(List<TItem> level)
            => level.Select(item =>
                buildNode(item, childrenMap.TryGetValue(idSelector(item), out var kids) ? Build(kids) : null)
            ).ToList();

        return Build(roots);
    }
}
