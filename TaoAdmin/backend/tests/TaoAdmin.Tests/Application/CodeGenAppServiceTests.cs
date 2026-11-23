using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 代码生成器（⑲）应用服务（SQLite 内存库真库测试）：
/// 设计校验、同步建库（建表/加列自动、破坏性只出语句）、一键生成全套代码（标记头/记录落库）、记录查询。
/// </summary>
public class CodeGenAppServiceTests
{
    private static (CodeGenAppService Svc, SqlSugarClient Db) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysCodeGenTable, SysCodeGenRecord>();
        var svc = new CodeGenAppService(db, new SnowflakeIdGenerator(1, 1));
        return (svc, db);
    }

    private static CodegenDesignSaveRequest ValidDesign(string tableName = "biz_contract", string displayName = "合同管理")
        => new()
        {
            tableName = tableName,
            displayName = displayName,
            fields = new List<CodegenFieldDesign>
            {
                new() { name = "title", label = "合同标题", dataType = "string", length = 100, nullable = false, control = "text", queryType = "like" },
                new() { name = "amount", label = "金额", dataType = "decimal", precision = 2, nullable = true, control = "number" },
                new() { name = "sign_date", label = "签订日期", dataType = "datetime", nullable = true, control = "date" },
                new() { name = "level", label = "级别", dataType = "int", nullable = true, control = "dict", dictCode = "contract_level" }
            }
        };


    private static HashSet<string> GetColumns(SqlSugarClient db, string table)
    {
        var conn = (Microsoft.Data.Sqlite.SqliteConnection)db.Ado.Connection;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(`{table}`)";
        using var reader = cmd.ExecuteReader();
        var set = new HashSet<string>();
        while (reader.Read()) set.Add(reader.GetString(1).ToLowerInvariant());
        return set;
    }

    [Fact]
    public async Task SaveDesign_ShouldCreate_AndUpdate()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        Assert.True(id > 0);
        var row = await svc.GetAsync(id);
        Assert.Equal("biz_contract", row.TableName);
        Assert.Equal(4, row.ParseFields().Count);

        // 更新：改中文名
        var req = ValidDesign();
        req.id = id;
        req.displayName = "合同管理V2";
        await svc.SaveDesignAsync(req);
        Assert.Equal("合同管理V2", (await svc.GetAsync(id)).DisplayName);
    }

    [Fact]
    public async Task SaveDesign_InvalidInputs_ShouldThrow()
    {
        var (svc, _) = Create();
        // 表名空
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(ValidDesign(tableName: "")));
        // 表名格式（大写/连字符不允许）
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(ValidDesign(tableName: "Biz-Contract")));
        // 中文名空
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(ValidDesign(displayName: " ")));
        // 字段清单空
        var noFields = ValidDesign(); noFields.fields.Clear();
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(noFields));
        // 字段名重复
        var dup = ValidDesign();
        dup.fields.Add(new CodegenFieldDesign { name = "title", label = "重复", dataType = "string", length = 50 });
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(dup));
        // 不支持的类型
        var badType = ValidDesign();
        badType.fields[0].dataType = "float";
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(badType));
        // 字典控件缺字典编码
        var noDict = ValidDesign();
        noDict.fields[3].dictCode = null;
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(noDict));
    }

    [Fact]
    public async Task SaveDesign_DuplicateTableName_ShouldThrow()
    {
        var (svc, _) = Create();
        await svc.SaveDesignAsync(ValidDesign());
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(ValidDesign()));
    }

    [Fact]
    public async Task SyncToDatabase_ShouldCreateTable_WithAllColumns()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        var result = await svc.SyncToDatabaseAsync(id);
        Assert.True(result.executed);
        Assert.Contains(result.actions, a => a.Contains("已建表 biz_contract"));

        // 实际列存在性（含基类四列）
        var cols = GetColumns(db, "biz_contract");
        Assert.True(cols.IsSupersetOf(new[] { "id", "title", "amount", "sign_date", "level", "created_at", "updated_at", "is_deleted" }));
    }

    [Fact]
    public async Task SyncToDatabase_SecondRun_IsIdempotent()
    {
        var (svc, _) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        await svc.SyncToDatabaseAsync(id);
        var second = await svc.SyncToDatabaseAsync(id);
        Assert.False(second.executed);
        Assert.Empty(second.actions);
        Assert.Empty(second.destructivePending);
    }

    [Fact]
    public async Task SyncToDatabase_AddField_ShouldAutoAddColumn()
    {
        var (svc, _) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        await svc.SyncToDatabaseAsync(id);

        var req = ValidDesign();
        req.id = id;
        req.fields.Add(new CodegenFieldDesign { name = "remark", label = "备注", dataType = "text", nullable = true, control = "textarea" });
        await svc.SaveDesignAsync(req);

        var result = await svc.SyncToDatabaseAsync(id);
        Assert.True(result.executed);
        Assert.Contains(result.actions, a => a.Contains("已加列 remark"));
    }

    [Fact]
    public async Task SyncToDatabase_RemovedField_ShouldReturnDestructiveSql_NotExecute()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        await svc.SyncToDatabaseAsync(id);

        // 设计里移除 amount 列
        var req = ValidDesign();
        req.id = id;
        req.fields.RemoveAll(f => f.name == "amount");
        await svc.SaveDesignAsync(req);

        var result = await svc.SyncToDatabaseAsync(id);
        Assert.False(result.executed);
        Assert.Single(result.destructivePending);
        Assert.Contains("DROP COLUMN `amount`", result.destructivePending[0]);

        // 列还在库里（未被删）
        var cols = GetColumns(db, "biz_contract");
        Assert.Contains("amount", cols);
    }

    [Fact]
    public async Task ExecuteDestructive_ShouldRejectNonAlter_AndMultiStatement()
    {
        var (svc, _) = Create();
        await Assert.ThrowsAsync<DomainException>(() => svc.ExecuteDestructiveAsync("DELETE FROM biz_contract"));
        await Assert.ThrowsAsync<DomainException>(() => svc.ExecuteDestructiveAsync("ALTER TABLE t DROP COLUMN a; DROP TABLE t;"));
    }

    [Fact]
    public async Task Generate_ShouldProduceFullStack_WithMarkerHeaders_AndRecord()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        var result = await svc.GenerateAsync(id);

        Assert.Equal(7, result.files.Count);
        Assert.Equal("biz_contract", result.tableName);
        Assert.True(result.elapsedMs >= 0);

        // 后端实体：类名、表名、字段齐全
        var entity = result.files.First(f => f.path.EndsWith("/BizContract.cs"));
        Assert.Contains("class BizContract : BaseEntity", entity.content);
        Assert.Contains("[SugarTable(\"biz_contract\"", entity.content);
        Assert.Contains("public string Title { get; set; }", entity.content);
        Assert.Contains("public decimal? Amount { get; set; }", entity.content);

        // 服务：分页/保存/删除/批量删 + 关键字查询（like 字段）
        var service = result.files.First(f => f.path.EndsWith("BizContractAppService.cs"));
        Assert.Contains("public async Task<PageResult<BizContract>> PageAsync", service.content);
        Assert.Contains("t => t.Title.Contains(keyword!)", service.content);
        Assert.Contains("BatchDeleteAsync", service.content);

        // 扩展文件：手写区不被覆盖
        var ext = result.files.First(f => f.path.EndsWith("BizContractAppService.Extensions.cs"));
        Assert.Contains("EXTENSION FILE", ext.content);
        Assert.Contains("partial class BizContractAppService", ext.content);

        // 控制器：路由 + 权限码
        var ctrl = result.files.First(f => f.path.EndsWith("BizContractController.cs"));
        Assert.Contains("[Route(\"biz_contract\")]", ctrl.content);
        Assert.Contains("[Permission(\"gen:biz_contract:manage\")]", ctrl.content);

        // 前端：接口定义 + 列表页
        var api = result.files.First(f => f.path.EndsWith("/api/bizContract.ts"));
        Assert.Contains("export function pageBizContract", api.content);
        var page = result.files.First(f => f.path.EndsWith("views/bizContract/index.vue"));
        Assert.Contains("a-table", page.content);
        Assert.Contains("ant-design-vue", page.content);
        Assert.Contains("批量删除", page.content);
        Assert.Contains("/identity/biz_contract", api.content);

        // 生成文件全部带标记头（SQL 初始化文件除外）
        foreach (var f in result.files.Where(f => f.path.EndsWith(".cs") || f.path.EndsWith(".ts") || f.path.EndsWith(".vue")))
            Assert.Contains("CODEGEN", f.content);

        // 记录落库
        var records = db.Queryable<SysCodeGenRecord>().ToList();
        Assert.Single(records);
        Assert.Equal(7, records[0].FileCount);
        Assert.Equal("generate", records[0].Mode);
    }

    [Fact]
    public async Task Generate_RegenerateMode_ShouldMarkRecord()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        await svc.GenerateAsync(id);
        await svc.GenerateAsync(id, "regenerate");
        var modes = db.Queryable<SysCodeGenRecord>().OrderBy(r => r.CreatedAt).ToList().Select(r => r.Mode).ToList();
        Assert.Equal(new[] { "generate", "regenerate" }, modes);
    }

    [Fact]
    public async Task PageRecords_ShouldFilterByKeyword()
    {
        var (svc, _) = Create();
        var id1 = await svc.SaveDesignAsync(ValidDesign());
        var id2 = await svc.SaveDesignAsync(ValidDesign(tableName: "biz_order", displayName: "订单管理"));
        await svc.GenerateAsync(id1);
        await svc.GenerateAsync(id2);

        var all = await svc.PageRecordsAsync(1, 10, null);
        Assert.Equal(2, all.Total);
        var filtered = await svc.PageRecordsAsync(1, 10, "biz_order");
        Assert.Equal(1, filtered.Total);
        Assert.Equal("biz_order", filtered.Items[0].TableName);
    }

    [Fact]
    public async Task GetRecord_ShouldReturnFiles()
    {
        var (svc, _) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        var gen = await svc.GenerateAsync(id);
        var (record, files) = await svc.GetRecordAsync(gen.recordId);
        Assert.Equal(7, files.Count);
        Assert.All(files, f => Assert.False(string.IsNullOrWhiteSpace(f.content)));
    }

    [Fact]
    public async Task DeleteDesign_ShouldSoftDelete()
    {
        var (svc, _) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        await svc.DeleteAsync(id);
        await Assert.ThrowsAsync<DomainException>(() => svc.GetAsync(id));
    }

    [Fact]
    public async Task Generate_EmptyFields_ShouldThrow()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        // 模拟脏数据：直接把设计字段清空（绕过保存校验）
        var row = db.Queryable<SysCodeGenTable>().First(t => t.Id == id);
        row.FieldsJson = "[]";
        db.Updateable(row).ExecuteCommand();
        await Assert.ThrowsAsync<DomainException>(() => svc.GenerateAsync(id));
    }
}
