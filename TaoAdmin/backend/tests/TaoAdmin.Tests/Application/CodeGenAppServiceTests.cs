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

    /// <summary>读取真实落库的列类型与默认值（PRAGMA table_info：2=type，4=dflt_value）</summary>
    private static Dictionary<string, (string Type, string? Default)> GetColumnInfos(SqlSugarClient db, string table)
    {
        var conn = (Microsoft.Data.Sqlite.SqliteConnection)db.Ado.Connection;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(`{table}`)";
        using var reader = cmd.ExecuteReader();
        var dict = new Dictionary<string, (string, string?)>();
        while (reader.Read())
            dict[reader.GetString(1).ToLowerInvariant()] = (reader.GetString(2), reader.IsDBNull(4) ? null : reader.GetString(4));
        return dict;
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

    // ==================== 补账班：分支覆盖清零（v3.20 硬门禁） ====================

    private static CodegenDesignSaveRequest WithField(CodegenDesignSaveRequest req, CodegenFieldDesign f)
    {
        req.fields.Add(f);
        return req;
    }

    [Fact]
    public async Task SaveDesign_FieldNameValidation_AllBranches()
    {
        var (svc, _) = Create();
        // 字段名空白（|| 短路分支）
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveDesignAsync(WithField(ValidDesign(), new CodegenFieldDesign { name = "  ", label = "空名", dataType = "string", length = 10 })));
        // 字段名不合法（大写开头，正则分支）
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveDesignAsync(WithField(ValidDesign(), new CodegenFieldDesign { name = "Title", label = "大写", dataType = "string", length = 10 })));
        // 控件不支持
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveDesignAsync(WithField(ValidDesign(), new CodegenFieldDesign { name = "radio_f", label = "单选", dataType = "string", length = 10, control = "radio" })));
        // 查询方式不支持
        await Assert.ThrowsAsync<DomainException>(() =>
            svc.SaveDesignAsync(WithField(ValidDesign(), new CodegenFieldDesign { name = "fuzzy_f", label = "模糊", dataType = "string", length = 10, queryType = "fuzzy" })));
        // string 长度：null / 0 / 4001
        foreach (var bad in new int?[] { null, 0, 4001 })
            await Assert.ThrowsAsync<DomainException>(() =>
                svc.SaveDesignAsync(WithField(ValidDesign(), new CodegenFieldDesign { name = $"len_{bad ?? 0}f", label = "长度", dataType = "string", length = bad })));
        // decimal 小数位：null / -1 / 11
        foreach (var bad in new int?[] { null, -1, 11 })
            await Assert.ThrowsAsync<DomainException>(() =>
                svc.SaveDesignAsync(WithField(ValidDesign(), new CodegenFieldDesign { name = $"prec_{bad ?? 0}f", label = "小数", dataType = "decimal", precision = bad })));
    }

    [Fact]
    public async Task SyncAndGenerate_AllTypes_Indexes_Defaults_Controls()
    {
        var (svc, db) = Create();
        var req = new CodegenDesignSaveRequest
        {
            tableName = "biz_full",
            displayName = "全类型",
            fields = new List<CodegenFieldDesign>
            {
                new() { name = "title", label = "标题", dataType = "string", length = 50, nullable = true, defaultValue = "o'brien", control = "text" },
                new() { name = "code", label = "编码", dataType = "string", length = 20, nullable = false, control = "text" },
                new() { name = "content", label = "内容", dataType = "text", nullable = true, control = "textarea" },
                new() { name = "cnt", label = "数量", dataType = "int", nullable = false, defaultValue = "5", control = "number" },
                new() { name = "big_id", label = "大数", dataType = "long", nullable = true, defaultValue = "123", control = "number", isIndex = true },
                new() { name = "price", label = "价格", dataType = "decimal", precision = 2, nullable = true, defaultValue = "1.50", control = "number" },
                new() { name = "sign_date", label = "日期", dataType = "datetime", nullable = true, control = "date" },
                new() { name = "flag", label = "开关", dataType = "bool", nullable = false, defaultValue = "true", control = "switch" },
                new() { name = "active", label = "启用", dataType = "bool", nullable = true, defaultValue = "1", control = "switch" },
                new() { name = "extra", label = "附加", dataType = "bool", nullable = true, defaultValue = "yes", control = "switch", isUnique = true }
            }
        };
        var id = await svc.SaveDesignAsync(req);

        // 同步建库：索引（普通 + 唯一）真实落库
        var sync = await svc.SyncToDatabaseAsync(id);
        Assert.True(sync.executed);
        // 注意：db.Ado.Connection 是共享连接（:memory: 库随连接存亡），不可 dispose
        var conn = (Microsoft.Data.Sqlite.SqliteConnection)db.Ado.Connection;
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_biz_full%'";
            using var reader = cmd.ExecuteReader();
            var idx = new HashSet<string>();
            while (reader.Read()) idx.Add(reader.GetString(0));
            Assert.Contains("idx_biz_full_big_id", idx);
            Assert.Contains("idx_biz_full_extra", idx);
        }

        // 一键生成：各类型 DDL/实体特性/前端控件全分支
        var gen = await svc.GenerateAsync(id);

        // DDL 事实断真实落库（PRAGMA table_info：类型与默认值）
        var infos = GetColumnInfos(db, "biz_full");
        Assert.Equal("bigint", infos["big_id"].Type);                          // long 列类型
        Assert.Equal("boolean", infos["flag"].Type);                           // bool 列类型
        Assert.Equal("5", infos["cnt"].Default);                               // 数值默认值
        Assert.Equal("1", infos["flag"].Default);                              // bool true/1 → 1
        Assert.Equal("0", infos["extra"].Default);                             // bool 其他 → 0
        Assert.Equal("'o''brien'", infos["title"].Default);                    // 字符串默认值转义

        // 实体文件断 C# 类型与特性
        var entity = gen.files.First(f => f.path.EndsWith("/BizFull.cs"));
        Assert.Contains("public long? BigId", entity.content);                 // long 类型 + 可空
        Assert.Contains("public bool Flag", entity.content);                   // bool 类型
        Assert.Contains("= string.Empty;", entity.content);                    // 可空 string 初始化
        Assert.Contains("ColumnDataType = \"TEXT\"", entity.content);          // text 列特性
        Assert.Contains("DecimalDigits = 2", entity.content);                  // decimal 特性
        var api = gen.files.First(f => f.path.EndsWith("/api/bizFull.ts"));
        Assert.Contains("boolean", api.content);                               // TsType bool 分支
        var page = gen.files.First(f => f.path.EndsWith("views/bizFull/index.vue"));
        Assert.Contains("a-textarea", page.content);                           // textarea 控件
        Assert.Contains("a-switch", page.content);                             // switch 控件
        Assert.Contains("false", page.content);                                // TsInit bool 分支
    }

    [Fact]
    public async Task DirtyData_UnknownType_AndNullLength_ShouldTakeDefaultBranches()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        // 模拟脏数据：未知类型 + string 无长度（绕过保存校验直写库）
        // prec：decimal 非空 → 补 GenEntity decimal 臂内层三元的 false 侧
        var row = db.Queryable<SysCodeGenTable>().First(t => t.Id == id);
        row.FieldsJson = """[{"name":"weird","label":"怪","dataType":"float","nullable":true,"control":"text","queryType":"none"},{"name":"nolen","label":"无长","dataType":"string","length":null,"nullable":true,"control":"text","queryType":"none"},{"name":"prec","label":"精","dataType":"decimal","precision":2,"nullable":false,"control":"number","queryType":"none"}]""";
        db.Updateable(row).ExecuteCommand();

        // 同步：BuildColumnSql 默认分支（nvarchar(200)）+ string 无长度分支
        var sync = await svc.SyncToDatabaseAsync(id);
        Assert.True(sync.executed);
        var cols = GetColumns(db, "biz_contract");
        Assert.Contains("weird", cols);
        Assert.Contains("nolen", cols);
        Assert.Contains("prec", cols);

        // 生成：GenEntity/CsType/TsType/TsInit 默认分支
        var gen = await svc.GenerateAsync(id);
        var entity = gen.files.First(f => f.path.EndsWith("/BizContract.cs"));
        // 未知类型走 CsType 默认分支 → string；nullable → 可空标记
        Assert.Contains("public string? Weird { get; set; }", entity.content);
        var api = gen.files.First(f => f.path.EndsWith("/api/bizContract.ts"));
        Assert.Contains("weird: string", api.content);
    }

    /// <summary>模拟 MySQL 方言：方言判断强制走 information_schema 分支，底层仍是同一 SQLite 连接</summary>
    private class MySqlDialectService(SqlSugarClient db) : CodeGenAppService(db, new SnowflakeIdGenerator(1, 1))
    {
        protected override bool IsSqliteDialect => false;
    }

    [Fact]
    public async Task SyncToDatabase_MySqlDialect_InformationSchema_Path()
    {
        var (svc, db) = Create();
        // 用 SQLite 挂载 information_schema 模式，真实走 MySQL 方言分支（information_schema 探测）
        db.Ado.ExecuteCommand("ATTACH DATABASE ':memory:' AS `information_schema`");
        db.Ado.ExecuteCommand("CREATE TABLE `information_schema`.`tables` (table_name TEXT)");
        db.Ado.ExecuteCommand("CREATE TABLE `information_schema`.`columns` (table_name TEXT, column_name TEXT)");
        var mysqlSvc = new MySqlDialectService(db);

        var id = await svc.SaveDesignAsync(ValidDesign());
        // 首次同步：information_schema.tables 为空 → 走建表
        var first = await mysqlSvc.SyncToDatabaseAsync(id);
        Assert.True(first.executed);
        Assert.Contains(first.actions, a => a.Contains("已建表 biz_contract"));

        // 登记真实列，模拟 information_schema.columns
        var cols = GetColumns(db, "biz_contract");
        foreach (var c in cols)
            db.Ado.ExecuteCommand($"INSERT INTO `information_schema`.`columns` (table_name, column_name) VALUES ('biz_contract', '{c}')");
        db.Ado.ExecuteCommand("INSERT INTO `information_schema`.`tables` (table_name) VALUES ('biz_contract')");

        // 加字段再同步：表存在（非 SQLite 探测）+ 列清单来自 information_schema → 走加列
        var req = ValidDesign();
        req.id = id;
        req.fields.Add(new CodegenFieldDesign { name = "remark", label = "备注", dataType = "text", nullable = true, control = "textarea" });
        await svc.SaveDesignAsync(req);

        var second = await mysqlSvc.SyncToDatabaseAsync(id);
        Assert.True(second.executed);
        Assert.Contains(second.actions, a => a.Contains("已加列 remark"));
        Assert.Empty(second.destructivePending);
    }

    [Fact]
    public async Task ExecuteDestructive_ValidAlter_ShouldApply()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        await svc.SyncToDatabaseAsync(id);

        await svc.ExecuteDestructiveAsync("ALTER TABLE `biz_contract` DROP COLUMN `amount`");
        var cols = GetColumns(db, "biz_contract");
        Assert.DoesNotContain("amount", cols);
    }

    [Fact]
    public async Task PageDesigns_WithAndWithoutKeyword()
    {
        var (svc, _) = Create();
        await svc.SaveDesignAsync(ValidDesign());
        await svc.SaveDesignAsync(ValidDesign(tableName: "biz_order", displayName: "订单管理"));

        var all = await svc.PageAsync(1, 10, null);
        Assert.Equal(2, all.Total);
        // 关键字命中中文名
        var byName = await svc.PageAsync(1, 10, "订单");
        Assert.Equal(1, byName.Total);
        // 关键字命中表名
        var byTable = await svc.PageAsync(1, 10, "biz_contract");
        Assert.Equal(1, byTable.Total);
        Assert.Equal("biz_contract", byTable.Items[0].TableName);
    }

    [Fact]
    public async Task SaveDesign_UpdateNonexistent_ShouldThrow()
    {
        var (svc, _) = Create();
        var req = ValidDesign();
        req.id = 999;
        await Assert.ThrowsAsync<DomainException>(() => svc.SaveDesignAsync(req));
    }

    [Fact]
    public async Task SyncToDatabase_EmptyFields_ShouldThrow()
    {
        var (svc, db) = Create();
        var id = await svc.SaveDesignAsync(ValidDesign());
        var row = db.Queryable<SysCodeGenTable>().First(t => t.Id == id);
        row.FieldsJson = "[]";
        db.Updateable(row).ExecuteCommand();
        await Assert.ThrowsAsync<DomainException>(() => svc.SyncToDatabaseAsync(id));
    }

    [Fact]
    public async Task GetRecord_NotFound_AndBlankFiles()
    {
        var (svc, db) = Create();
        // 记录不存在
        await Assert.ThrowsAsync<DomainException>(() => svc.GetRecordAsync(999));
        // FilesJson 为 null / 空串 / 空白 → 空文件清单；JSON 字面量 null → 反序列化为 null → 兜底空清单
        var id = await svc.SaveDesignAsync(ValidDesign());
        foreach (var blank in new string?[] { null, "", "   " })
        {
            var rec = new SysCodeGenRecord { Id = id + (blank == null ? 1 : blank == "" ? 2 : 3), TableId = id, TableName = "biz_contract", FilesJson = blank };
            await db.Insertable(rec).ExecuteCommandAsync();
            var (_, files) = await svc.GetRecordAsync(rec.Id);
            Assert.Empty(files);
        }
        var nullLit = new SysCodeGenRecord { Id = id + 4, TableId = id, TableName = "biz_contract", FilesJson = "null" };
        await db.Insertable(nullLit).ExecuteCommandAsync();
        var (_, nullFiles) = await svc.GetRecordAsync(nullLit.Id);
        Assert.Empty(nullFiles);
    }

    [Fact]
    public void ParseFields_Null_InvalidJson_AndNullLiteral()
    {
        // FieldsJson 为 null
        var e1 = new SysCodeGenTable { FieldsJson = null };
        Assert.Empty(e1.ParseFields());
        // 非法 JSON（JsonException 分支）
        var e2 = new SysCodeGenTable { FieldsJson = "not json" };
        Assert.Empty(e2.ParseFields());
        // JSON 字面量 null（反序列化为 null → 兜底空清单）
        var e3 = new SysCodeGenTable { FieldsJson = "null" };
        Assert.Empty(e3.ParseFields());
    }
}
