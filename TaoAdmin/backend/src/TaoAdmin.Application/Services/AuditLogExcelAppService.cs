using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using MiniExcelLibs;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 审计日志导出服务（⑮）：按当前筛选条件导出操作/登录日志为 xlsx。
/// 上限 5 万条（与导出场景匹配，超出提示收窄筛选）；导出动作由控制器留痕。
/// </summary>
public class AuditLogExcelAppService(ISqlSugarClient db)
{
    /// <summary>单次导出上限，防止误操作拉爆内存</summary>
    public const int MaxExportRows = 50_000;

    /// <summary>按筛选条件导出日志（与分页查询同一套筛选口径；begin/end 闭区间）</summary>
    public async Task<(byte[] Content, int Count)> ExportAsync(
        string? category, string? userName, int? success, DateTime? begin, DateTime? end)
    {
        var query = db.Queryable<SysOperationLog>()
            .Where(l => l.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(category), l => l.Category == category)
            .WhereIF(!string.IsNullOrWhiteSpace(userName), l => l.UserName.Contains(userName!))
            .WhereIF(success.HasValue, l => l.Success == success!.Value)
            .WhereIF(begin.HasValue, l => l.CreatedAt >= begin!.Value)
            .WhereIF(end.HasValue, l => l.CreatedAt <= end!.Value);

        var rows = await query
            .OrderBy(l => l.CreatedAt, OrderByType.Desc)
            .OrderBy(l => l.Id, OrderByType.Desc)
            .Take(MaxExportRows)
            .ToListAsync();

        var dict = rows.Select(l => new Dictionary<string, object>
        {
            ["时间"] = l.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ["类别"] = l.Category ?? "",
            ["账号"] = l.UserName ?? "",
            ["结果"] = l.Success == 1 ? "成功" : "失败",
            ["方法"] = l.Method ?? "",
            ["路径"] = l.Path ?? "",
            ["状态码"] = l.StatusCode,
            ["IP"] = l.Ip ?? "",
            ["耗时(ms)"] = l.ElapsedMs,
            ["详情"] = l.Detail ?? ""
        }).ToList();

        using var ms = new MemoryStream();
        await MiniExcel.SaveAsAsync(ms, dict, sheetName: "审计日志");
        return (ms.ToArray(), dict.Count);
    }
}
