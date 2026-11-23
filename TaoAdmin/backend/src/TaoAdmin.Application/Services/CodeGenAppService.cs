using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>表设计保存请求</summary>
public class CodegenDesignSaveRequest
{
    /// <summary>设计 Id（新建传空）</summary>
    public long? id { get; set; }
    /// <summary>表名（英文下划线风格）</summary>
    public string tableName { get; set; } = "";
    /// <summary>中文名</summary>
    public string displayName { get; set; } = "";
    /// <summary>字段设计清单</summary>
    public List<CodegenFieldDesign> fields { get; set; } = new();
    /// <summary>备注</summary>
    public string? remark { get; set; }
}

/// <summary>同步结果：执行了什么、跳过了什么、待确认的破坏性语句</summary>
public class CodegenSyncResult
{
    /// <summary>是否执行了变更</summary>
    public bool executed { get; set; }
    /// <summary>执行说明（建表/加列清单）</summary>
    public List<string> actions { get; set; } = new();
    /// <summary>破坏性变更语句（删列/改类型）：仅返回不执行，经确认后另行执行</summary>
    public List<string> destructivePending { get; set; } = new();
}

/// <summary>生成结果：文件清单 + 记录 Id</summary>
public class CodegenGenerateResult
{
    public long recordId { get; set; }
    public string tableName { get; set; } = "";
    /// <summary>生成出的文件（相对路径 → 内容，内容带统一标记头）</summary>
    public List<CodegenFile> files { get; set; } = new();
    public long elapsedMs { get; set; }
}

/// <summary>生成出的单个文件</summary>
public class CodegenFile
{
    public string path { get; set; } = "";
    public string content { get; set; } = "";
}

/// <summary>
/// 代码生成器应用服务（⑲）：
/// 表设计维护 → 同步建库（加列自动同步；删列/改类型等破坏性变更仅生成语句经确认后执行）
/// → 一键生成全套增删改查前后端代码（实体/服务/接口/权限登记 + 前端接口定义/列表页/表单弹窗）。
/// 生成文件带统一标记头，重复生成只覆盖生成区；手改逻辑写扩展文件（部分类）不被冲掉。
/// </summary>
public class CodeGenAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
{
    /// <summary>生成文件统一标记头：标识该文件为生成区，重复生成会被覆盖</summary>
    public const string MarkerHeader = "// ==== CODEGEN GENERATED FILE（生成区：重复生成会覆盖本文件；手改逻辑请写 *AppService.Extensions.cs 扩展文件） ====";

    private static readonly string[] AllowedTypes = { "string", "int", "long", "decimal", "datetime", "bool", "text" };
    private static readonly string[] AllowedControls = { "text", "number", "date", "textarea", "dict", "switch" };
    private static readonly string[] AllowedQueries = { "none", "eq", "like", "range" };

    // ==================== 设计维护 ====================

    /// <summary>保存表设计（新建或更新）</summary>
    public async Task<long> SaveDesignAsync(CodegenDesignSaveRequest req)
    {
        ValidateDesign(req);
        SysCodeGenTable row;
        if (req.id is long id && id > 0)
        {
            row = await db.Queryable<SysCodeGenTable>().FirstAsync(t => t.Id == id && t.IsDeleted == false)
                ?? throw new DomainException("表设计不存在或已删除");
        }
        else
        {
            var dup = await db.Queryable<SysCodeGenTable>().AnyAsync(t => t.TableName == req.tableName && t.IsDeleted == false);
            if (dup) throw new DomainException($"表名 {req.tableName} 的设计已存在，请直接编辑原设计");
            row = new SysCodeGenTable { Id = snowflake.NextId() };
        }
        row.TableName = req.tableName.Trim();
        row.DisplayName = req.displayName.Trim();
        row.FieldsJson = JsonSerializer.Serialize(req.fields);
        row.Remark = req.remark;
        row.UpdatedAt = DateTime.Now;
        if (row.Id != 0 && await db.Queryable<SysCodeGenTable>().AnyAsync(t => t.Id == row.Id))
            await db.Updateable(row).ExecuteCommandAsync();
        else
            await db.Insertable(row).ExecuteCommandAsync();
        return row.Id;
    }

    /// <summary>设计分页</summary>
    public async Task<PageResult<SysCodeGenTable>> PageAsync(int pageIndex, int pageSize, string? keyword)
    {
        RefAsync<int> total = 0;
        var list = await db.Queryable<SysCodeGenTable>()
            .Where(t => t.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), t => t.TableName.Contains(keyword!) || t.DisplayName.Contains(keyword!))
            .OrderByDescending(t => t.UpdatedAt)
            .ToPageListAsync(pageIndex, pageSize, total);
        return new PageResult<SysCodeGenTable> { Items = list, Total = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>设计详情</summary>
    public async Task<SysCodeGenTable> GetAsync(long id)
        => await db.Queryable<SysCodeGenTable>().FirstAsync(t => t.Id == id && t.IsDeleted == false)
           ?? throw new DomainException("表设计不存在或已删除");

    /// <summary>删除设计（软删，不动已生成的数据库表与代码）</summary>
    public async Task DeleteAsync(long id)
    {
        var row = await GetAsync(id);
        row.IsDeleted = true;
        row.UpdatedAt = DateTime.Now;
        await db.Updateable(row).ExecuteCommandAsync();
    }

    // ==================== 同步建库 ====================

    /// <summary>
    /// 同步到数据库：表不存在则建表；已存在则只自动加缺失列（加字段自动同步）。
    /// 删列、改类型等破坏性变更不自动执行——生成语句放进 destructivePending，经确认后另行执行。
    /// </summary>
    public async Task<CodegenSyncResult> SyncToDatabaseAsync(long tableId)
    {
        var design = await GetAsync(tableId);
        var fields = design.ParseFields();
        if (fields.Count == 0) throw new DomainException("字段清单为空，请先在设计器里画字段");
        var result = new CodegenSyncResult();
        var table = design.TableName;

        if (!await TableExistsAsync(table))
        {
            var createSql = BuildCreateTableSql(table, fields);
            await db.Ado.ExecuteCommandAsync(createSql);
            result.executed = true;
            result.actions.Add($"已建表 {table}（{fields.Count} 个业务字段）");
        }
        else
        {
            var existing = await GetExistingColumnsAsync(table);
            foreach (var f in fields)
            {
                if (existing.Contains(f.name.ToLowerInvariant())) continue;
                var colSql = BuildColumnSql(f);
                await db.Ado.ExecuteCommandAsync($"ALTER TABLE `{table}` ADD COLUMN {colSql};");
                result.executed = true;
                result.actions.Add($"已加列 {f.name}（{f.dataType}）");
            }
            // 破坏性变更：库里有、设计里没有的列 → 只生成语句不执行
            var designNames = fields.Select(f => f.name.ToLowerInvariant()).ToHashSet();
            foreach (var col in existing)
            {
                if (designNames.Contains(col) || col == "id" || col == "created_at" || col == "updated_at" || col == "is_deleted") continue;
                result.destructivePending.Add($"-- 删除列 {col}（设计里已移除，确认后执行）：\nALTER TABLE `{table}` DROP COLUMN `{col}`;");
            }
        }

        design.LastSyncAt = DateTime.Now;
        design.UpdatedAt = DateTime.Now;
        await db.Updateable(design).ExecuteCommandAsync();
        return result;
    }

    /// <summary>执行经确认的破坏性变更语句（仅允许单条 ALTER TABLE）</summary>
    public async Task ExecuteDestructiveAsync(string sql)
    {
        var trimmed = sql.Trim().TrimEnd(';');
        if (!trimmed.StartsWith("ALTER TABLE", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("仅允许执行 ALTER TABLE 语句");
        if (trimmed.Contains(';'))
            throw new DomainException("一次只允许执行一条语句");
        await db.Ado.ExecuteCommandAsync(trimmed + ";");
    }

    // ==================== 一键生成 ====================

    /// <summary>一键生成全套增删改查前后端代码，落生成记录</summary>
    public async Task<CodegenGenerateResult> GenerateAsync(long tableId, string mode = "generate")
    {
        var sw = Stopwatch.StartNew();
        var design = await GetAsync(tableId);
        var fields = design.ParseFields();
        if (fields.Count == 0) throw new DomainException("字段清单为空，请先在设计器里画字段");

        var entityName = ToPascal(design.TableName);
        var camel = ToCamel(entityName);
        var files = new List<CodegenFile>
        {
            new() { path = $"backend/src/TaoAdmin.Domain/Entities/{entityName}.cs", content = GenEntity(design, fields) },
            new() { path = $"backend/src/TaoAdmin.Application/Services/{entityName}AppService.cs", content = GenAppService(design, fields, entityName) },
            new() { path = $"backend/src/TaoAdmin.Application/Services/{entityName}AppService.Extensions.cs", content = GenExtensionStub(entityName) },
            new() { path = $"backend/src/TaoAdmin.IdentityService/Controllers/{entityName}Controller.cs", content = GenController(design, fields, entityName) },
            new() { path = $"frontend/src/api/{camel}.ts", content = GenFrontendApi(design, fields, entityName, camel) },
            new() { path = $"frontend/src/views/{camel}/index.vue", content = GenFrontendPage(design, fields, entityName, camel) },
        };

        // 权限与菜单初始化数据（SQL）：装上即可在角色里勾选
        files.Add(new CodegenFile
        {
            path = $"generated/{design.TableName}/menu-permission-init.sql",
            content = GenMenuPermissionSql(design, entityName)
        });

        sw.Stop();
        var record = new SysCodeGenRecord
        {
            Id = snowflake.NextId(),
            TableId = tableId,
            TableName = design.TableName,
            Mode = mode == "regenerate" ? "regenerate" : "generate",
            FilesJson = JsonSerializer.Serialize(files),
            FileCount = files.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        await db.Insertable(record).ExecuteCommandAsync();

        return new CodegenGenerateResult { recordId = record.Id, tableName = design.TableName, files = files, elapsedMs = sw.ElapsedMilliseconds };
    }

    /// <summary>生成记录分页</summary>
    public async Task<PageResult<SysCodeGenRecord>> PageRecordsAsync(int pageIndex, int pageSize, string? keyword)
    {
        RefAsync<int> total = 0;
        var list = await db.Queryable<SysCodeGenRecord>()
            .Where(r => r.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), r => r.TableName.Contains(keyword!))
            .OrderByDescending(r => r.CreatedAt)
            .ToPageListAsync(pageIndex, pageSize, total);
        return new PageResult<SysCodeGenRecord> { Items = list, Total = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    /// <summary>生成记录详情（含全部文件内容）</summary>
    public async Task<(SysCodeGenRecord Record, List<CodegenFile> Files)> GetRecordAsync(long id)
    {
        var record = await db.Queryable<SysCodeGenRecord>().FirstAsync(r => r.Id == id && r.IsDeleted == false)
            ?? throw new DomainException("生成记录不存在");
        var files = string.IsNullOrWhiteSpace(record.FilesJson)
            ? new List<CodegenFile>()
            : JsonSerializer.Deserialize<List<CodegenFile>>(record.FilesJson) ?? new List<CodegenFile>();
        return (record, files);
    }

    // ==================== 校验 ====================

    private static void ValidateDesign(CodegenDesignSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.tableName)) throw new DomainException("表名不能为空");
        if (!Regex.IsMatch(req.tableName.Trim(), @"^[a-z][a-z0-9_]{2,98}$"))
            throw new DomainException("表名须为小写字母开头的下划线风格（3-99 位，仅小写字母/数字/下划线）");
        if (string.IsNullOrWhiteSpace(req.displayName)) throw new DomainException("中文名不能为空");
        if (req.fields.Count == 0) throw new DomainException("至少要有一个业务字段");
        var names = new HashSet<string>();
        foreach (var f in req.fields)
        {
            if (string.IsNullOrWhiteSpace(f.name) || !Regex.IsMatch(f.name.Trim(), @"^[a-z][a-z0-9_]{1,63}$"))
                throw new DomainException($"字段名 {f.name} 不合法（小写字母开头的下划线风格）");
            if (!names.Add(f.name.Trim())) throw new DomainException($"字段名 {f.name} 重复");
            if (!AllowedTypes.Contains(f.dataType)) throw new DomainException($"字段 {f.name} 的类型 {f.dataType} 不支持");
            if (!AllowedControls.Contains(f.control)) throw new DomainException($"字段 {f.name} 的控件 {f.control} 不支持");
            if (!AllowedQueries.Contains(f.queryType)) throw new DomainException($"字段 {f.name} 的查询方式 {f.queryType} 不支持");
            if (f.control == "dict" && string.IsNullOrWhiteSpace(f.dictCode))
                throw new DomainException($"字段 {f.name} 选了字典控件，必须填关联字典编码");
            if (f.dataType == "string" && (f.length is null or < 1 or > 4000))
                throw new DomainException($"字段 {f.name} 的长度须在 1-4000 之间");
            if (f.dataType == "decimal" && (f.precision is null or < 0 or > 10))
                throw new DomainException($"字段 {f.name} 的小数位须在 0-10 之间");
        }
    }

    // ==================== DDL 构建 ====================

    /// <summary>方言判断缝隙：SQLite 走 sqlite_master/PRAGMA，其余走 information_schema（测试可覆写模拟 MySQL 方言）</summary>
    protected virtual bool IsSqliteDialect => db.CurrentConnectionConfig.DbType == SqlSugar.DbType.Sqlite;

    private async Task<bool> TableExistsAsync(string table)
    {
        if (IsSqliteDialect)
        {
            var conn = (Microsoft.Data.Sqlite.SqliteConnection)db.Ado.Connection;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=@name";
            cmd.Parameters.AddWithValue("@name", table);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        var n = await db.Ado.SqlQueryAsync<int>($"SELECT COUNT(1) FROM information_schema.tables WHERE table_name = '{table.Replace("'", "''")}'");
        return n.FirstOrDefault() > 0;
    }

    private async Task<HashSet<string>> GetExistingColumnsAsync(string table)
    {
        var set = new HashSet<string>();
        if (IsSqliteDialect)
        {
            var conn = (Microsoft.Data.Sqlite.SqliteConnection)db.Ado.Connection;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(`{table.Replace("`", "``")}`)";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) set.Add(reader.GetString(1).ToLowerInvariant());
        }
        else
        {
            var cols = await db.Ado.SqlQueryAsync<string>($"SELECT LOWER(column_name) FROM information_schema.columns WHERE table_name = '{table.Replace("'", "''")}'");
            foreach (var c in cols) set.Add(c);
        }
        return set;
    }

    private static string BuildCreateTableSql(string table, List<CodegenFieldDesign> fields)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE `").Append(table).Append("` (\n");
        sb.AppendLine("  `id` bigint NOT NULL PRIMARY KEY,");
        foreach (var f in fields) sb.Append("  ").Append(BuildColumnSql(f)).Append(",\n");
        sb.AppendLine("  `created_at` datetime NOT NULL,");
        sb.AppendLine("  `updated_at` datetime NOT NULL,");
        sb.AppendLine("  `is_deleted` boolean NOT NULL DEFAULT 0");
        sb.AppendLine(");");
        foreach (var f in fields.Where(f => f.isIndex || f.isUnique))
            sb.AppendLine($"CREATE {(f.isUnique ? "UNIQUE " : "")}INDEX `idx_{table}_{f.name}` ON `{table}` (`{f.name}`);");
        return sb.ToString();
    }

    private static string BuildColumnSql(CodegenFieldDesign f)
    {
        var type = f.dataType switch
        {
            "string" => f.length is int l ? $"nvarchar({l})" : "nvarchar(200)",
            "text" => "TEXT",
            "int" => "int",
            "long" => "bigint",
            "decimal" => $"decimal(18,{f.precision ?? 2})",
            "datetime" => "datetime",
            "bool" => "boolean",
            _ => "nvarchar(200)"
        };
        var nullable = f.nullable ? " NULL" : " NOT NULL";
        var def = string.IsNullOrWhiteSpace(f.defaultValue) ? "" : $" DEFAULT {FormatDefault(f)}";
        return $"`{f.name}` {type}{nullable}{def}";
    }

    private static string FormatDefault(CodegenFieldDesign f) => f.dataType switch
    {
        "int" or "long" or "decimal" => f.defaultValue!.Trim(),
        "bool" => f.defaultValue!.Trim() == "true" || f.defaultValue!.Trim() == "1" ? "1" : "0",
        _ => $"'{f.defaultValue!.Replace("'", "''")}'"
    };

    // ==================== 代码模板 ====================

    private static string ToPascal(string name)
        => string.Concat(name.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(s => char.ToUpperInvariant(s[0]) + s[1..]));

    private static string ToCamel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    private static string CsType(CodegenFieldDesign f) => f.dataType switch
    {
        "int" => "int",
        "long" => "long",
        "decimal" => "decimal",
        "datetime" => "DateTime",
        "bool" => "bool",
        _ => "string"
    };

    private static string NullableCs(CodegenFieldDesign f)
        => f.nullable ? CsType(f) + "?" : CsType(f);

    private static string GenEntity(SysCodeGenTable design, List<CodegenFieldDesign> fields)
    {
        var entityName = ToPascal(design.TableName);
        var sb = new StringBuilder();
        sb.AppendLine(MarkerHeader);
        sb.AppendLine("using SqlSugar;");
        sb.AppendLine("using TaoAdmin.Common.Entities;");
        sb.AppendLine();
        sb.AppendLine("namespace TaoAdmin.Domain.Entities;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// {design.DisplayName}（{design.TableName}）：由代码生成器生成，手改逻辑请写扩展文件。");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"[SugarTable(\"{design.TableName}\", TableDescription = \"{design.DisplayName}\")]");
        sb.AppendLine($"public class {entityName} : BaseEntity");
        sb.AppendLine("{");
        foreach (var f in fields)
        {
            var colAttr = f.dataType switch
            {
                "string" => $"[SugarColumn(Length = {f.length ?? 200}, {(f.nullable ? "IsNullable = true, " : "")}ColumnDescription = \"{f.label}\")]",
                "text" => $"[SugarColumn(ColumnDataType = \"TEXT\", IsNullable = true, ColumnDescription = \"{f.label}\")]",
                "decimal" => $"[SugarColumn(DecimalDigits = {f.precision ?? 2}, {(f.nullable ? "IsNullable = true, " : "")}ColumnDescription = \"{f.label}\")]",
                _ => $"[SugarColumn({(f.nullable ? "IsNullable = true, " : "")}ColumnDescription = \"{f.label}\")]"
            };
            sb.AppendLine($"    /// <summary>{f.label}</summary>");
            sb.AppendLine($"    {colAttr}");
            sb.AppendLine($"    public {NullableCs(f)} {ToPascal(f.name)} {{ get; set; }}{(f.nullable && (f.dataType == "string") ? " = string.Empty;" : "")}");
            sb.AppendLine();
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenAppService(SysCodeGenTable design, List<CodegenFieldDesign> fields, string entityName)
    {
        var camel = ToCamel(entityName);
        var sb = new StringBuilder();
        sb.AppendLine(MarkerHeader);
        sb.AppendLine("using TaoAdmin.Common.Http;");
        sb.AppendLine("using TaoAdmin.Domain;");
        sb.AppendLine("using TaoAdmin.Domain.Entities;");
        sb.AppendLine("using SqlSugar;");
        sb.AppendLine();
        sb.AppendLine("namespace TaoAdmin.Application.Services;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>{design.DisplayName} 应用服务（生成区）：分页/详情/增/改/删/批量删。业务个性逻辑写 {entityName}AppService.Extensions.cs。</summary>");
        sb.AppendLine($"public partial class {entityName}AppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake)");
        sb.AppendLine("{");
        // 分页
        sb.AppendLine($"    /// <summary>分页查询</summary>");
        sb.AppendLine($"    public async Task<PageResult<{entityName}>> PageAsync(int pageIndex, int pageSize, string? keyword)");
        sb.AppendLine("    {");
        sb.AppendLine("        RefAsync<int> total = 0;");
        var likeField = fields.FirstOrDefault(f => f.queryType == "like");
        sb.AppendLine($"        var list = await db.Queryable<{entityName}>()");
        sb.AppendLine("            .Where(t => t.IsDeleted == false)");
        if (likeField != null)
            sb.AppendLine($"            .WhereIF(!string.IsNullOrWhiteSpace(keyword), t => t.{ToPascal(likeField.name)}.Contains(keyword!))");
        sb.AppendLine("            .OrderByDescending(t => t.Id)");
        sb.AppendLine("            .ToPageListAsync(pageIndex, pageSize, total);");
        sb.AppendLine("        return new PageResult<" + entityName + "> { Items = list, Total = total, PageIndex = pageIndex, PageSize = pageSize };");
        sb.AppendLine("    }");
        sb.AppendLine();
        // 详情
        sb.AppendLine($"    /// <summary>详情</summary>");
        sb.AppendLine($"    public async Task<{entityName}> GetAsync(long id)");
        sb.AppendLine($"        => await db.Queryable<{entityName}>().FirstAsync(t => t.Id == id && t.IsDeleted == false)");
        sb.AppendLine($"           ?? throw new DomainException(\"{design.DisplayName}不存在或已删除\");");
        sb.AppendLine();
        // 保存（增/改）
        var reqProps = string.Join("\n", fields.Select(f => $"    /// <summary>{f.label}</summary>\n    public {NullableCs(f)} {ToPascal(f.name)} {{ get; set; }}"));
        sb.AppendLine($"/// <summary>{design.DisplayName} 保存请求</summary>");
        sb.AppendLine($"public class {entityName}SaveRequest");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Id（更新时传）</summary>");
        sb.AppendLine("    public long? id { get; set; }");
        sb.AppendLine(reqProps);
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>保存（新增或更新）</summary>");
        sb.AppendLine($"    public async Task<long> SaveAsync({entityName}SaveRequest req)");
        sb.AppendLine("    {");
        sb.AppendLine($"        {entityName} row;");
        sb.AppendLine("        if (req.id is long id && id > 0)");
        sb.AppendLine("        {");
        sb.AppendLine($"            row = await db.Queryable<{entityName}>().FirstAsync(t => t.Id == id && t.IsDeleted == false)");
        sb.AppendLine($"                ?? throw new DomainException(\"{design.DisplayName}不存在或已删除\");");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine($"            row = new {entityName} {{ Id = snowflake.NextId() }};");
        sb.AppendLine("        }");
        foreach (var f in fields)
            sb.AppendLine($"        row.{ToPascal(f.name)} = req.{ToPascal(f.name)};");
        sb.AppendLine("        row.UpdatedAt = DateTime.Now;");
        sb.AppendLine("        if (req.id is long uid && uid > 0)");
        sb.AppendLine("            await db.Updateable(row).ExecuteCommandAsync();");
        sb.AppendLine("        else");
        sb.AppendLine("            await db.Insertable(row).ExecuteCommandAsync();");
        sb.AppendLine("        return row.Id;");
        sb.AppendLine("    }");
        sb.AppendLine();
        // 删除
        sb.AppendLine($"    /// <summary>删除（软删）</summary>");
        sb.AppendLine($"    public async Task DeleteAsync(long id)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var row = await GetAsync(id);");
        sb.AppendLine("        row.IsDeleted = true;");
        sb.AppendLine("        row.UpdatedAt = DateTime.Now;");
        sb.AppendLine("        await db.Updateable(row).ExecuteCommandAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>批量删除（软删，返回实际删除数）</summary>");
        sb.AppendLine($"    public async Task<int> BatchDeleteAsync(List<long> ids)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (ids == null || ids.Count == 0) return 0;");
        sb.AppendLine($"        return await db.Updateable<{entityName}>()");
        sb.AppendLine("            .SetColumns(t => t.IsDeleted == true)");
        sb.AppendLine("            .SetColumns(t => t.UpdatedAt == DateTime.Now)");
        sb.AppendLine("            .Where(t => ids.Contains(t.Id) && t.IsDeleted == false)");
        sb.AppendLine("            .ExecuteCommandAsync();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenExtensionStub(string entityName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// ==== CODEGEN EXTENSION FILE（手写扩展区：本文件不会被重新生成覆盖，业务个性逻辑写在这里） ====");
        sb.AppendLine("using SqlSugar;");
        sb.AppendLine("using TaoAdmin.Common.Snowflake;");
        sb.AppendLine();
        sb.AppendLine("namespace TaoAdmin.Application.Services;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>{entityName} 手写扩展区：跨表报表、复杂流程、特殊查询等个性逻辑放这里。</summary>");
        sb.AppendLine($"public partial class {entityName}AppService");
        sb.AppendLine("{");
        sb.AppendLine($"    // 示例：public async Task<object> MyBusinessQueryAsync(...) {{ ... }}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenController(SysCodeGenTable design, List<CodegenFieldDesign> fields, string entityName)
    {
        var perm = $"gen:{design.TableName}:manage";
        var sb = new StringBuilder();
        sb.AppendLine(MarkerHeader);
        sb.AppendLine("using TaoAdmin.Application.Services;");
        sb.AppendLine("using TaoAdmin.Common.Http;");
        sb.AppendLine("using TaoAdmin.Common.Web;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine();
        sb.AppendLine("namespace TaoAdmin.IdentityService.Controllers;");
        sb.AppendLine();
        sb.AppendLine($"/// <summary>{design.DisplayName}管理（代码生成器生成）</summary>");
        sb.AppendLine($"[Route(\"{design.TableName}\")]");
        sb.AppendLine($"public class {entityName}Controller : BaseApiController");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {entityName}AppService _svc;");
        sb.AppendLine($"    public {entityName}Controller({entityName}AppService svc) => _svc = svc;");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>分页查询</summary>");
        sb.AppendLine($"    [HttpGet]");
        sb.AppendLine($"    [Permission(\"{perm}\")]");
        sb.AppendLine($"    public async Task<ApiResult<PageResult<{entityName}>>> Page([FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)");
        sb.AppendLine($"        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, keyword));");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>详情</summary>");
        sb.AppendLine($"    [HttpGet(\"{{id}}\")]");
        sb.AppendLine($"    [Permission(\"{perm}\")]");
        sb.AppendLine($"    public async Task<ApiResult<{entityName}>> Get(string id)");
        sb.AppendLine($"        => ApiOk(await _svc.GetAsync(long.Parse(id)));");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>保存（新增或更新）</summary>");
        sb.AppendLine($"    [HttpPost]");
        sb.AppendLine($"    [Permission(\"{perm}\")]");
        sb.AppendLine($"    public async Task<ApiResult<object>> Save([FromBody] {entityName}SaveRequest req)");
        sb.AppendLine("    {");
        sb.AppendLine("        var id = await _svc.SaveAsync(req);");
        sb.AppendLine("        return ApiOk(id > 0 ? id.ToString() : \"保存成功\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>删除（软删）</summary>");
        sb.AppendLine($"    [HttpDelete(\"{{id}}\")]");
        sb.AppendLine($"    [Permission(\"{perm}\")]");
        sb.AppendLine($"    public async Task<ApiResult<object>> Delete(string id)");
        sb.AppendLine("    {");
        sb.AppendLine("        await _svc.DeleteAsync(long.Parse(id));");
        sb.AppendLine($"        return ApiOk(\"删除成功\");");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    /// <summary>批量删除（软删，返回实际删除数）</summary>");
        sb.AppendLine($"    [HttpPost(\"batch-delete\")]");
        sb.AppendLine($"    [Permission(\"{perm}\")]");
        sb.AppendLine($"    public async Task<ApiResult<object>> BatchDelete([FromBody] BatchIdsRequest req)");
        sb.AppendLine($"        => ApiOk($\"已删除 {{await _svc.BatchDeleteAsync(req.Ids)}} 条\");");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenFrontendApi(SysCodeGenTable design, List<CodegenFieldDesign> fields, string entityName, string camel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// ==== CODEGEN GENERATED FILE（生成区：重复生成会覆盖本文件） ====");
        sb.AppendLine($"// {design.DisplayName} 接口定义");
        sb.AppendLine("import { request } from './request';");
        sb.AppendLine();
        sb.AppendLine($"export interface {entityName}Item {{");
        sb.AppendLine("  id: string;");
        foreach (var f in fields)
            sb.AppendLine($"  {f.name}: {TsType(f)};");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"export interface {entityName}Save {{");
        sb.AppendLine("  id?: string;");
        foreach (var f in fields)
            sb.AppendLine($"  {f.name}: {TsType(f)};");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** 分页查询 */");
        sb.AppendLine($"export function page{entityName}(params: {{ pageIndex?: number; pageSize?: number; keyword?: string }}): Promise<{{ total: number; items: {entityName}Item[] }}> {{");
        sb.AppendLine($"  return request({{ url: '/identity/{design.TableName}', method: 'get', params }});");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** 详情 */");
        sb.AppendLine($"export function get{entityName}(id: string): Promise<{entityName}Item> {{");
        sb.AppendLine($"  return request({{ url: `/identity/{design.TableName}/${{id}}`, method: 'get' }});");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** 保存（新增或更新） */");
        sb.AppendLine($"export function save{entityName}(data: {entityName}Save): Promise<void> {{");
        sb.AppendLine($"  return request({{ url: '/identity/{design.TableName}', method: 'post', data: {{ ...data, id: data.id ? Number(data.id) : undefined }} }});");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** 删除（单条） */");
        sb.AppendLine($"export function delete{entityName}(id: string): Promise<void> {{");
        sb.AppendLine($"  return request({{ url: `/identity/{design.TableName}/${{id}}`, method: 'delete' }});");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** 批量删除 */");
        sb.AppendLine($"export function batchDelete{entityName}(ids: string[]): Promise<{{ deleted: number }}> {{");
        sb.AppendLine($"  return request({{ url: '/identity/{design.TableName}/batch-delete', method: 'post', data: {{ ids: ids.map(Number) }} }});");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string TsType(CodegenFieldDesign f) => f.dataType switch
    {
        "int" or "long" or "decimal" => "number",
        "bool" => "boolean",
        _ => "string"
    };

    private static string GenFrontendPage(SysCodeGenTable design, List<CodegenFieldDesign> fields, string entityName, string camel)
    {
        var listFields = fields.Where(f => f.showInList).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("<!-- ==== CODEGEN GENERATED FILE（生成区：重复生成会覆盖本文件） ====");
        sb.AppendLine($"     {design.DisplayName} 列表页：分页 + 搜索 + 表单弹窗（增/改）+ 批量删除（Ant Design Vue 4） -->");
        sb.AppendLine("<template>");
        sb.AppendLine("  <div class=\"page-container\">");
        sb.AppendLine("    <!-- 搜索与操作区 -->");
        sb.AppendLine("    <div class=\"table-operator\">");
        sb.AppendLine("      <a-input v-model:value=\"keyword\" placeholder=\"搜索\" allow-clear style=\"width: 220px\" @press-enter=\"loadData()\" />");
        sb.AppendLine("      <a-button type=\"primary\" style=\"margin-left: 8px\" @click=\"loadData()\">查询</a-button>");
        sb.AppendLine("      <a-button type=\"primary\" style=\"margin-left: 12px\" @click=\"openCreate\">新增</a-button>");
        sb.AppendLine("      <a-space v-if=\"selectedRowKeys.length > 0\" style=\"margin-left: 12px\">");
        sb.AppendLine("        <span>已选 {{ selectedRowKeys.length }} 项</span>");
        sb.AppendLine("        <a-button danger @click=\"onBatchDelete\">批量删除</a-button>");
        sb.AppendLine("      </a-space>");
        sb.AppendLine("    </div>");
        sb.AppendLine();
        sb.AppendLine("    <!-- 表格 -->");
        sb.AppendLine("    <a-table");
        sb.AppendLine("      :data-source=\"rows\"");
        sb.AppendLine("      :columns=\"columns\"");
        sb.AppendLine("      :loading=\"loading\"");
        sb.AppendLine("      row-key=\"id\"");
        sb.AppendLine("      size=\"middle\"");
        sb.AppendLine("      :pagination=\"pagination\"");
        sb.AppendLine("      :row-selection=\"{ selectedRowKeys, onChange: (k: any[]) => (selectedRowKeys = k) }\"");
        sb.AppendLine("      @change=\"onTableChange\"");
        sb.AppendLine("    >");
        sb.AppendLine("      <template #bodyCell=\"{ column, record }\">");
        sb.AppendLine("        <template v-if=\"column.key === 'action'\">");
        sb.AppendLine("          <a-space>");
        sb.AppendLine("            <a @click=\"openEdit(record)\">编辑</a>");
        sb.AppendLine("            <a-popconfirm title=\"确定删除该条记录吗？\" @confirm=\"onDelete(record)\">");
        sb.AppendLine("              <a class=\"danger-link\">删除</a>");
        sb.AppendLine("            </a-popconfirm>");
        sb.AppendLine("          </a-space>");
        sb.AppendLine("        </template>");
        sb.AppendLine("        <template v-else-if=\"boolColumns.includes(column.key)\">");
        sb.AppendLine("          {{ record[column.key] ? '是' : '否' }}");
        sb.AppendLine("        </template>");
        sb.AppendLine("      </template>");
        sb.AppendLine("    </a-table>");
        sb.AppendLine();
        sb.AppendLine("    <!-- 新增/编辑弹窗 -->");
        sb.AppendLine("    <a-modal v-model:open=\"dialogVisible\" :title=\"form.id ? '编辑' : '新增'\" :confirm-loading=\"saving\" @ok=\"onSave\">");
        sb.AppendLine("      <a-form :model=\"form\" layout=\"vertical\">");
        foreach (var f in fields)
        {
            var required = f.nullable ? "" : " required";
            var ctrl = f.control switch
            {
                "number" => $"<a-input-number v-model:value=\"form.{f.name}\" style=\"width: 100%\" />",
                "date" => $"<a-date-picker v-model:value=\"form.{f.name}\" show-time value-format=\"YYYY-MM-DD HH:mm:ss\" style=\"width: 100%\" />",
                "textarea" => $"<a-textarea v-model:value=\"form.{f.name}\" :rows=\"3\" />",
                "dict" => $"<a-input v-model:value=\"form.{f.name}\" placeholder=\"字典 {f.dictCode}\" />",
                "switch" => $"<a-switch v-model:checked=\"form.{f.name}\" />",
                _ => $"<a-input v-model:value=\"form.{f.name}\" />"
            };
            sb.AppendLine($"        <a-form-item label=\"{f.label}\" name=\"{f.name}\"{required}>");
            sb.AppendLine($"          {ctrl}");
            sb.AppendLine("        </a-form-item>");
        }
        sb.AppendLine("      </a-form>");
        sb.AppendLine("    </a-modal>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</template>");
        sb.AppendLine();
        sb.AppendLine("<script setup lang=\"ts\">");
        sb.AppendLine($"import {{ page{entityName}, get{entityName}, save{entityName}, delete{entityName}, batchDelete{entityName} }} from '@/api/{camel}'");
        sb.AppendLine("import { message, Modal } from 'ant-design-vue'");
        sb.AppendLine("import { ref, reactive } from 'vue'");
        sb.AppendLine();
        var colItems = listFields.Select(f => $"{{ title: '{f.label}', dataIndex: '{f.name}', key: '{f.name}' }}");
        sb.AppendLine($"const columns = [");
        foreach (var c in colItems)
            sb.AppendLine($"  {c},");
        sb.AppendLine("  { title: '操作', key: 'action', width: 140 },");
        sb.AppendLine("];");
        var boolCols = listFields.Where(f => f.dataType == "bool").Select(f => $"'{f.name}'");
        sb.AppendLine($"const boolColumns = [{string.Join(", ", boolCols)}];");
        sb.AppendLine();
        sb.AppendLine("const rows = ref<any[]>([])");
        sb.AppendLine("const loading = ref(false)");
        sb.AppendLine("const saving = ref(false)");
        sb.AppendLine("const keyword = ref('')");
        sb.AppendLine("const selectedRowKeys = ref<any[]>([])");
        sb.AppendLine("const dialogVisible = ref(false)");
        sb.AppendLine("const pagination = reactive({ current: 1, pageSize: 10, total: 0, showSizeChanger: true, showTotal: (t: number) => `共 ${t} 条` })");
        sb.AppendLine($"const emptyForm = () => ({{ id: undefined, {string.Join(", ", fields.Select(f => $"{f.name}: {TsInit(f)}"))} }})");
        sb.AppendLine("const form = ref<any>(emptyForm())");
        sb.AppendLine();
        sb.AppendLine("async function loadData() {");
        sb.AppendLine("  loading.value = true");
        sb.AppendLine("  try {");
        sb.AppendLine($"    const res = await page{entityName}({{ pageIndex: pagination.current, pageSize: pagination.pageSize, keyword: keyword.value || undefined }})");
        sb.AppendLine("    rows.value = res.items");
        sb.AppendLine("    pagination.total = res.total");
        sb.AppendLine("  } finally {");
        sb.AppendLine("    loading.value = false");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function onTableChange(p: any) {");
        sb.AppendLine("  pagination.current = p.current");
        sb.AppendLine("  pagination.pageSize = p.pageSize");
        sb.AppendLine("  loadData()");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function openCreate() {");
        sb.AppendLine("  form.value = emptyForm()");
        sb.AppendLine("  dialogVisible.value = true");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("async function openEdit(row: any) {");
        sb.AppendLine($"  form.value = await get{entityName}(row.id)");
        sb.AppendLine("  dialogVisible.value = true");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("async function onSave() {");
        sb.AppendLine("  saving.value = true");
        sb.AppendLine("  try {");
        sb.AppendLine($"    await save{entityName}(form.value)");
        sb.AppendLine("    message.success('保存成功')");
        sb.AppendLine("    dialogVisible.value = false");
        sb.AppendLine("    await loadData()");
        sb.AppendLine("  } finally {");
        sb.AppendLine("    saving.value = false");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function onDelete(row: any) {");
        sb.AppendLine($"  return delete{entityName}(row.id).then(() => {{");
        sb.AppendLine("    message.success('删除成功')");
        sb.AppendLine("    return loadData()");
        sb.AppendLine("  })");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("function onBatchDelete() {");
        sb.AppendLine("  Modal.confirm({");
        sb.AppendLine("    title: '批量删除',");
        sb.AppendLine($"    content: `确认删除选中的 ${{selectedRowKeys.value.length}} 条记录？`,");
        sb.AppendLine("    onOk: async () => {");
        sb.AppendLine($"      const res = await batchDelete{entityName}(selectedRowKeys.value)");
        sb.AppendLine("      message.success(`已删除 ${res.deleted} 条`)");
        sb.AppendLine("      selectedRowKeys.value = []");
        sb.AppendLine("      await loadData()");
        sb.AppendLine("    }");
        sb.AppendLine("  })");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("loadData()");
        sb.AppendLine("</script>");
        return sb.ToString();
    }

    private static string TsInit(CodegenFieldDesign f) => f.dataType switch
    {
        "int" or "long" or "decimal" => "0",
        "bool" => "false",
        _ => "''"
    };

    private static string GenMenuPermissionSql(SysCodeGenTable design, string entityName)
    {
        var perm = $"gen:{design.TableName}:manage";
        return $@"-- {design.DisplayName}：菜单与按钮权限初始化（装上后在角色管理里勾选）
-- 菜单（挂在系统管理下，parent_id 请按实际菜单 Id 调整）
INSERT INTO sys_menu (id, parent_id, name, path, component, menu_type, permission, sort, is_deleted, created_at, updated_at)
VALUES ({{雪花ID}}, 0, '{design.DisplayName}', '/{design.TableName}', '{ToCamel(entityName)}/index', 1, NULL, 100, 0, NOW(), NOW());
-- 按钮权限
INSERT INTO sys_menu (id, parent_id, name, path, component, menu_type, permission, sort, is_deleted, created_at, updated_at)
VALUES ({{雪花ID}}, {{上级菜单ID}}, '{design.DisplayName}管理', '', '', 2, '{perm}', 1, 0, NOW(), NOW());";
    }
}
