using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using MiniExcelLibs;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 用户表格导入导出应用服务：通用 Excel 能力先接用户核心数据。
/// 导出走 MiniExcel 生成 xlsx；导入逐行校验，错误行给出明细不中断整体。
/// </summary>
public class UserExcelAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake, IPasswordPolicyProvider policy)
{
    /// <summary>导入模板列（与导出列一致，密码列导出时留空不落真实口令）</summary>
    public static readonly string[] Columns = { "用户名", "昵称", "邮箱", "手机号", "状态(启用/禁用)", "密码(仅导入,留空用默认密码)" };

    /// <summary>导入时密码留空使用的默认密码（满足内置默认策略：≥8 位且含 3 类字符）</summary>
    public const string DefaultImportPassword = "Aa@123456";

    // ---------- 导出 ----------

    /// <summary>导出全部用户为 xlsx 字节（密码列不导出；状态转中文）</summary>
    public async Task<byte[]> ExportAsync(string? keyword = null)
    {
        var query = db.Queryable<SysUser>().Where(u => u.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), u => u.UserName.Contains(keyword!.Trim()));
        var users = await query.OrderBy(u => u.CreatedAt).ToListAsync();
        var rows = users.Select(u => new Dictionary<string, object>
        {
            ["用户名"] = u.UserName,
            ["昵称"] = u.NickName ?? "",
            ["邮箱"] = u.Email ?? "",
            ["手机号"] = u.Phone ?? "",
            ["状态(启用/禁用)"] = u.Status == 1 ? "启用" : "禁用",
            ["密码(仅导入,留空用默认密码)"] = ""
        }).ToList();
        using var ms = new MemoryStream();
        await MiniExcel.SaveAsAsync(ms, rows, sheetName: "用户");
        return ms.ToArray();
    }

    /// <summary>生成导入模板（表头 + 一行示例）</summary>
    public async Task<byte[]> BuildTemplateAsync()
    {
        var sample = new Dictionary<string, object>
        {
            ["用户名"] = "zhangsan",
            ["昵称"] = "张三",
            ["邮箱"] = "zhangsan@example.com",
            ["手机号"] = "13800138000",
            ["状态(启用/禁用)"] = "启用",
            ["密码(仅导入,留空用默认密码)"] = ""
        };
        using var ms = new MemoryStream();
        await MiniExcel.SaveAsAsync(ms, new List<Dictionary<string, object>> { sample }, sheetName: "用户导入模板");
        return ms.ToArray();
    }

    // ---------- 导入 ----------

    /// <summary>导入用户：逐行校验（用户名必填/格式/重复、密码位数、状态取值），错误行给明细不中断。</summary>
    public async Task<UserImportResultDto> ImportAsync(Stream stream, long currentUserId)
    {
        var result = new UserImportResultDto();
        List<IDictionary<string, object?>> rows;
        try
        {
            // 同步 Query：默认读第一个工作表，首行作表头；动态行是 ExpandoObject，统一按字典接口访问
            rows = MiniExcel.Query(stream, true).Cast<IDictionary<string, object?>>().ToList();
        }
        catch (Exception)
        {
            throw new DomainException("表格解析失败：请使用系统提供的导入模板填写", 400);
        }

        // 统一字典访问：MiniExcel 的动态行键来自表头
        string Cell(IDictionary<string, object?> row, string col)
            => row.TryGetValue(col, out var v) && v != null ? v.ToString()!.Trim() : "";

        // 文件内用户名去重
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validRows = new List<(SysUser User, int RowNo)>();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            int rowNo = i + 1;
            string userName = Cell(row, Columns[0]);
            string password = Cell(row, Columns[5]);
            string statusText = Cell(row, Columns[4]);

            // 空行（模板示例之外整行空白）跳过
            if (row.All(c => string.IsNullOrWhiteSpace(c.Value?.ToString())))
                continue;

            // total 口径 = 全部数据行（含错误行），与前端「共 X 行，成功 Y 条」文案一致
            result.total++;

            if (string.IsNullOrWhiteSpace(userName))
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = "用户名不能为空" });
                continue;
            }
            if (userName.Length > 50)
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = $"用户名「{userName}」超过 50 个字符" });
                continue;
            }
            if (!seen.Add(userName))
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = $"用户名「{userName}」在文件中重复" });
                continue;
            }
            // 密码列校验走当前策略：填了按填的验，留空按落库默认密码验（策略更严时默认密码同样拦下）
            var effectivePassword = password.Length > 0 ? password : DefaultImportPassword;
            var pwdError = await policy.CheckAsync(effectivePassword);
            if (pwdError != null)
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = $"用户名「{userName}」{pwdError}" });
                continue;
            }
            if (statusText.Length > 0 && statusText != "启用" && statusText != "禁用")
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = $"用户名「{userName}」状态列只能填 启用 或 禁用" });
                continue;
            }

            var email = Cell(row, Columns[2]);
            if (email.Length > 100)
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = $"用户名「{userName}」邮箱过长" });
                continue;
            }
            var phone = Cell(row, Columns[3]);
            if (phone.Length > 20)
            {
                result.errors.Add(new UserImportErrorDto { row = rowNo, reason = $"用户名「{userName}」手机号过长" });
                continue;
            }

            validRows.Add((new SysUser
            {
                Id = snowflake.NextId(),
                UserName = userName,
                Password = BCrypt.Net.BCrypt.HashPassword(password.Length > 0 ? password : DefaultImportPassword),
                NickName = Cell(row, Columns[1]) is { Length: > 0 } nick ? nick : userName,
                Email = email.Length > 0 ? email : null,
                Phone = phone.Length > 0 ? phone : null,
                Status = statusText == "禁用" ? 0 : 1
            }, rowNo));
        }

        // 库内重名校验：逐个查（导入量级为管理场景，可接受）
        var toInsert = new List<SysUser>();
        foreach (var (user, rowNo) in validRows)
        {
            if (await db.Queryable<SysUser>().AnyAsync(u => u.UserName == user.UserName && u.IsDeleted == false))
            {
                result.errors.Add(new UserImportErrorDto
                {
                    row = rowNo,
                    reason = $"用户名「{user.UserName}」已存在于系统"
                });
                continue;
            }
            toInsert.Add(user);
        }

        if (toInsert.Count > 0)
        {
            await db.Insertable(toInsert).ExecuteCommandAsync();
            result.success = toInsert.Count;
        }
        return result;
    }
}
