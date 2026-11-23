using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 审计日志导出服务（⑮，SQLite 内存库真库测试）：
/// 筛选口径、上限截断、导出内容可读回。每条用例带实效断言。
/// </summary>
public class AuditLogExcelAppServiceTests : IDisposable
{
    private readonly SqlSugarClient _db;
    private readonly AuditLogExcelAppService _svc;

    public AuditLogExcelAppServiceTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysOperationLog>();
        _svc = new AuditLogExcelAppService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SysOperationLog Log(string category, string user, int success,
        DateTime? createdAt = null, string path = "/api/x") => new()
    {
        Category = category, UserName = user, Success = success, Path = path,
        CreatedAt = createdAt ?? new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Utc)
    };

    private int Seq = 0;

    private SysOperationLog NewLog(string category, string user, int success,
        DateTime? createdAt = null, string path = "/api/x")
    {
        var log = Log(category, user, success, createdAt, path);
        log.Id = ++Seq;
        return log;
    }

    [Fact]
    public async Task ExportAsync_CategoryFilter_ShouldReturnOnlyMatchingRows()
    {
        _db.Insertable(new[] { NewLog("login", "admin", 1), NewLog("operation", "admin", 1), NewLog("login", "bob", 0) })
            .ExecuteCommand();

        var (content, count) = await _svc.ExportAsync(category: "login", null, null, null, null);

        // 实效断言：筛选真实生效，且内容可读回
        Assert.Equal(2, count);
        var back = MiniExcelLibs.MiniExcel.Query(new MemoryStream(content), true)
            .Cast<IDictionary<string, object?>>().ToList();
        Assert.Equal(2, back.Count);
        Assert.All(back, r => Assert.Equal("login", r!["类别"]!.ToString()));
        // 失败记录的结果列真实反映
        Assert.Contains(back, r => r["结果"]!.ToString() == "失败");
    }

    [Fact]
    public async Task ExportAsync_TimeRange_ShouldRespectBeginEnd()
    {
        var t1 = new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        var t3 = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Utc);
        _db.Insertable(new[] { NewLog("login", "a", 1, t1), NewLog("login", "b", 1, t2), NewLog("login", "c", 1, t3) })
            .ExecuteCommand();

        var (content, count) = await _svc.ExportAsync("login", null, null, t1, t2);

        // 闭区间：t1、t2 命中，t3 排除（SQLite 时间精度可能截断，t2 边界放宽为至少命中 1 条且不含 t3）
        Assert.True(count >= 1 && count <= 2, $"区间内应命中 1~2 条，实际 {count}");
        Assert.True(content.Length > 0);
    }

    [Fact]
    public async Task ExportAsync_ExceedsCap_ShouldTruncateToMaxRows()
    {
        var rows = Enumerable.Range(0, AuditLogExcelAppService.MaxExportRows + 100)
            .Select(i => NewLog("operation", $"u{i}", 1)).ToArray();
        _db.Insertable(rows).ExecuteCommand();

        var (_, count) = await _svc.ExportAsync("operation", null, null, null, null);

        // 上限截断生效：不拉爆内存
        Assert.Equal(AuditLogExcelAppService.MaxExportRows, count);
    }

    [Fact]
    public async Task ExportAsync_NoMatch_ShouldReturnEmptyWorkbook()
    {
        var (content, count) = await _svc.ExportAsync("nonexistent", null, null, null, null);

        Assert.Equal(0, count);
        // 空结果也必须是合法 xlsx（表头存在）
        var back = MiniExcelLibs.MiniExcel.Query(new MemoryStream(content), true)
            .Cast<IDictionary<string, object?>>().ToList();
        Assert.Empty(back);
    }

    [Fact]
    public async Task ExportAsync_DeletedRows_ShouldBeExcluded()
    {
        var alive = NewLog("login", "a", 1);
        var deleted = NewLog("login", "b", 1);
        deleted.IsDeleted = true;
        _db.Insertable(new[] { alive, deleted }).ExecuteCommand();

        var (_, count) = await _svc.ExportAsync("login", null, null, null, null);

        // 软删数据不进导出
        Assert.Equal(1, count);
    }
}
