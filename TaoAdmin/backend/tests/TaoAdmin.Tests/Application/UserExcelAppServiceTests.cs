using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 用户表格导入导出应用服务（SQLite 内存库真库测试）：
/// 模板生成、导出不含密码、导入逐行校验与错误明细。
/// </summary>
public class UserExcelAppServiceTests : IDisposable
{
    private readonly SqlSugarClient _db;
    private readonly UserExcelAppService _svc;
    private readonly MemoryStream _stream;

    public UserExcelAppServiceTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysUser>();
        _svc = new UserExcelAppService(_db, new SnowflakeIdGenerator(1, 1), new DefaultPolicyStub());
        _stream = new MemoryStream();
    }

    public void Dispose() => _stream.Dispose();

    [Fact]
    public async Task BuildTemplateAsync_ShouldProduceReadableXlsx_WithHeaderAndSampleRow()
    {
        var bytes = await _svc.BuildTemplateAsync();
        Assert.True(bytes.Length > 0);
        // 模板可被自身导入流程解析（表头 + 示例行）：示例行用户名 zhangsan 校验通过后为 1 行有效
        var ms = new MemoryStream(bytes);
        var result = await _svc.ImportAsync(ms, 1);
        Assert.Equal(1, result.total);
        Assert.Equal(1, result.success);
        Assert.Empty(result.errors);
        // 导入未指定密码：落默认密码哈希（非明文）
        var user = Assert.Single(_db.Queryable<SysUser>().ToList());
        Assert.Equal("zhangsan", user.UserName);
        Assert.NotEqual(UserExcelAppService.DefaultImportPassword, user.Password);
        Assert.True(BCrypt.Net.BCrypt.Verify(UserExcelAppService.DefaultImportPassword, user.Password));
    }

    [Fact]
    public async Task ExportAsync_ShouldContainUserName_ButNotPassword()
    {
        _db.Insertable(new SysUser
        {
            Id = 1, UserName = "admin", Password = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            NickName = "管理员", Status = 1
        }).ExecuteCommand();

        var bytes = await _svc.ExportAsync();
        // xlsx 是 zip 压缩流，不能在原始字节上做子串断言；用 MiniExcel 读回逐行验内容（首行作表头）
        var back = MiniExcelLibs.MiniExcel.Query(new MemoryStream(bytes), true)
            .Cast<IDictionary<string, object?>>().ToList();
        string Cell(IDictionary<string, object?> r, string col)
            => r.TryGetValue(col, out var v) && v != null ? v.ToString()!.Trim() : "";
        var adminRow = Assert.Single(back, r => Cell(r, "用户名") == "admin");
        Assert.Equal("管理员", Cell(adminRow, "昵称"));
        // 密码列恒为空：真实口令与哈希都不导出
        Assert.Equal("", Cell(adminRow, UserExcelAppService.Columns[5]));
    }

    [Fact]
    public async Task ImportAsync_DuplicateUsernameInFile_ShouldReportErrorRows()
    {
        var bytes = await _svc.BuildTemplateAsync();
        // 构造含重复用户名的表格：直接用 MiniExcel 写两行同用户名
        var rows = new List<Dictionary<string, object>>
        {
            new() { ["用户名"] = "lisi", ["昵称"] = "李四", ["邮箱"] = "lisi@example.com", ["手机号"] = "", ["状态(启用/禁用)"] = "启用", ["密码(仅导入,留空用默认密码)"] = "" },
            new() { ["用户名"] = "lisi", ["昵称"] = "李四二", ["邮箱"] = "", ["手机号"] = "", ["状态(启用/禁用)"] = "启用", ["密码(仅导入,留空用默认密码)"] = "" },
        };
        var outMs = new MemoryStream();
        await MiniExcelLibs.MiniExcel.SaveAsAsync(outMs, rows, sheetName: "用户导入模板");
        var result = await _svc.ImportAsync(new MemoryStream(outMs.ToArray()), 1);

        Assert.Equal(2, result.total);
        Assert.Equal(1, result.success);   // 首行成功
        var err = Assert.Single(result.errors);
        Assert.Contains("重复", err.reason);
    }

    [Fact]
    public async Task ImportAsync_ExistingUsernameInDb_ShouldNotOverwrite_AndReportError()
    {
        _db.Insertable(new SysUser
        {
            Id = 7, UserName = "wangwu", Password = BCrypt.Net.BCrypt.HashPassword("Aa@123456"),
            NickName = "老王", Status = 1
        }).ExecuteCommand();

        var outMs = new MemoryStream();
        await MiniExcelLibs.MiniExcel.SaveAsAsync(outMs, new List<Dictionary<string, object>>
        {
            new() { ["用户名"] = "wangwu", ["昵称"] = "新王", ["邮箱"] = "", ["手机号"] = "", ["状态(启用/禁用)"] = "启用", ["密码(仅导入,留空用默认密码)"] = "" },
        }, sheetName: "用户导入模板");
        var result = await _svc.ImportAsync(new MemoryStream(outMs.ToArray()), 1);

        Assert.Equal(0, result.success);
        var err = Assert.Single(result.errors);
        Assert.Contains("已存在", err.reason);
        // 原用户未被覆盖
        var user = _db.Queryable<SysUser>().First(u => u.UserName == "wangwu");
        Assert.Equal("老王", user.NickName);
    }

    [Fact]
    public async Task ImportAsync_ShortPassword_AndBadStatus_ShouldReportReasons()
    {
        var outMs = new MemoryStream();
        await MiniExcelLibs.MiniExcel.SaveAsAsync(outMs, new List<Dictionary<string, object>>
        {
            new() { ["用户名"] = "aa", ["昵称"] = "", ["邮箱"] = "", ["手机号"] = "", ["状态(启用/禁用)"] = "启用", ["密码(仅导入,留空用默认密码)"] = "123" },
            new() { ["用户名"] = "bb", ["昵称"] = "", ["邮箱"] = "", ["手机号"] = "", ["状态(启用/禁用)"] = "活着", ["密码(仅导入,留空用默认密码)"] = "" },
        }, sheetName: "用户导入模板");
        var result = await _svc.ImportAsync(new MemoryStream(outMs.ToArray()), 1);

        Assert.Equal(0, result.success);
        Assert.Equal(2, result.errors.Count);
        Assert.Contains(result.errors, e => e.reason.Contains("密码"));
        Assert.Contains(result.errors, e => e.reason.Contains("状态"));
        Assert.Equal(0, _db.Queryable<SysUser>().Count());
    }
}
