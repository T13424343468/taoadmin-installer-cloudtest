using Microsoft.AspNetCore.Http;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.IdentityService.Controllers;
using Xunit;

namespace TaoAdmin.Tests.IdentityService;

/// <summary>
/// 代码生成器控制器测试（SQLite 内存库真库）：
/// 全部入口（设计分页/保存/详情/删除/同步/破坏性执行/生成/记录分页/记录详情）走通，
/// 含破坏性执行缺 sql 键的防御分支与生成 mode 缺省分支。
/// </summary>
public class CodeGenControllerTests
{
    private readonly SqlSugarClient _db;
    private readonly CodeGenAppService _svc;
    private readonly CodeGenController _controller;

    public CodeGenControllerTests()
    {
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        _db.CodeFirst.InitTables<SysCodeGenTable, SysCodeGenRecord>();
        _svc = new CodeGenAppService(_db, new SnowflakeIdGenerator(1, 1));
        _controller = new CodeGenController(_svc)
        {
            ControllerContext = { HttpContext = new DefaultHttpContext() }
        };
    }

    private static CodegenDesignSaveRequest ValidDesign(string tableName = "biz_contract", string displayName = "合同管理")
        => new()
        {
            tableName = tableName,
            displayName = displayName,
            fields = new List<CodegenFieldDesign>
            {
                new() { name = "title", label = "合同标题", dataType = "string", length = 100, nullable = false, control = "text", queryType = "like" }
            }
        };

    [Fact]
    public async Task Designs_ShouldReturnPaged()
    {
        await _controller.SaveDesign(ValidDesign());
        await _controller.SaveDesign(ValidDesign(tableName: "biz_order", displayName: "订单管理"));

        var all = await _controller.Designs(1, 10);
        Assert.True(all.Success);
        Assert.Equal(2, all.Data!.Total);

        var filtered = await _controller.Designs(1, 10, "订单");
        Assert.Equal(1, filtered.Data!.Total);
    }

    [Fact]
    public async Task SaveDesign_GetDesign_DeleteDesign_FullCycle()
    {
        var saved = await _controller.SaveDesign(ValidDesign());
        Assert.True(saved.Success);
        var id = long.Parse(saved.Data!.ToString()!);

        var got = await _controller.GetDesign(id.ToString());
        Assert.Equal("biz_contract", got.Data!.TableName);

        var deleted = await _controller.DeleteDesign(id.ToString());
        Assert.True(deleted.Success);
        await Assert.ThrowsAsync<DomainException>(() => _svc.GetAsync(id));
    }

    [Fact]
    public async Task Sync_ShouldReturnExecutedResult()
    {
        var saved = await _controller.SaveDesign(ValidDesign());
        var id = long.Parse(saved.Data!.ToString()!);

        var sync = await _controller.Sync(id.ToString());
        Assert.True(sync.Success);
        Assert.True(sync.Data!.executed);
    }

    [Fact]
    public async Task ExecuteDestructive_WithAndWithoutSqlKey()
    {
        var saved = await _controller.SaveDesign(ValidDesign());
        var id = long.Parse(saved.Data!.ToString()!);
        await _svc.SyncToDatabaseAsync(id);

        // 缺 sql 键 → 空串 → 拒绝（防御分支）
        await Assert.ThrowsAsync<DomainException>(() => _controller.ExecuteDestructive(new Dictionary<string, string>()));

        // 带 sql → 真实执行（设计里只有 title 字段，删它）
        var ok = await _controller.ExecuteDestructive(new Dictionary<string, string> { ["sql"] = "ALTER TABLE `biz_contract` DROP COLUMN `title`" });
        Assert.True(ok.Success);
        Assert.Equal("已执行", ok.Message);
    }

    [Fact]
    public async Task Generate_WithAndWithoutMode()
    {
        var saved = await _controller.SaveDesign(ValidDesign());
        var id = long.Parse(saved.Data!.ToString()!);

        // mode 缺省 → "generate"
        var gen = await _controller.Generate(id.ToString());
        Assert.True(gen.Success);
        Assert.Equal(7, gen.Data!.files.Count);

        // mode = regenerate
        var regen = await _controller.Generate(id.ToString(), "regenerate");
        var lastMode = _db.Queryable<SysCodeGenRecord>().OrderBy(r => r.Id).ToList().Last().Mode;
        Assert.Equal("regenerate", lastMode);
    }

    [Fact]
    public async Task Records_And_RecordDetail()
    {
        var saved = await _controller.SaveDesign(ValidDesign());
        var id = long.Parse(saved.Data!.ToString()!);
        var gen = await _svc.GenerateAsync(id);

        var page = await _controller.Records(1, 10);
        Assert.True(page.Success);
        Assert.Equal(1, page.Data!.Total);

        var filtered = await _controller.Records(1, 10, "biz_contract");
        Assert.Equal(1, filtered.Data!.Total);

        var detail = await _controller.Record(gen.recordId.ToString());
        Assert.True(detail.Success);
        Assert.NotNull(detail.Data);
    }
}
