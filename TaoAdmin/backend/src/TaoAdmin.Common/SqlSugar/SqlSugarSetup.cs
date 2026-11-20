using TaoAdmin.Common.Entities;
using Microsoft.Extensions.Configuration;
using Serilog;
using SqlSugar;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// SqlSugar ORM 装配：
/// 1. AddSqlSugar 注册 ISqlSugarClient 单例（MySQL 连接）；
/// 2. 支持读写分离：写/事务自动走主库，查询自动按权重分发到从库；
/// 3. AOP 把执行的 SQL 写入 Serilog；
/// 4. 提供 RunCodeFirst 钩子，供各服务启动时建表 + 种子数据。
/// </summary>
public static class SqlSugarSetup
{
    /// <summary>
    /// 注册 SqlSugar 客户端。
    /// 连接串从 ConnectionStrings:MySql 读取（主库），环境变量 <c>ConnectionStrings__MySql</c> 可覆盖。
    /// 读写分离：ConnectionStrings:Slaves 配置从库连接串数组；
    /// ReadWriteSplitting:Enabled=true 且从库非空时启用（查询走从库，写入与事务走主库）。
    /// </summary>
    public static IServiceCollection AddSqlSugar(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ISqlSugarClient>(sp =>
        {
            var connStr = configuration.GetConnectionString("MySql")
                          ?? "server=localhost;port=3306;database=taoadmin;uid=root;pwd=123456;charset=utf8mb4;SslMode=none;";

            var config = new ConnectionConfig
            {
                ConnectionString = connStr,
                DbType = SqlSugar.DbType.MySql,
                IsAutoCloseConnection = true,     // 自动释放连接
                InitKeyType = InitKeyType.Attribute // 按特性（SugarColumn）映射主键
            };

            // ------------------------------------------------------------------
            // 读写分离：配置从库列表即启用（SqlSugar 自动路由）
            //   - 查询（Queryable）自动分发到从库，写/事务自动走主库；
            //   - 需要强制主库时用 db.ForceMaster(...)（见 ReadWriteExtensions）；
            //   - 显式指定从库可用 db.SlaveQueryable<T>()。
            // 【本地】Slaves 留空或 Enabled=false，全部走单库
            // 【线上】Slaves 配置 1~N 个只读从库，Enabled=true
            // ------------------------------------------------------------------
            var slaves = configuration.GetSection("ConnectionStrings:Slaves").GetChildren()
                .Select(s => s.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            var splitEnabled = configuration.GetValue<bool>("ReadWriteSplitting:Enabled");

            if (splitEnabled && slaves.Count > 0)
            {
                config.SlaveConnectionConfigs = slaves
                    .Select(s => new SlaveConnectionConfig { ConnectionString = s })
                    .ToList();

                Log.Information("数据库读写分离已启用：主库 1 个，从库 {SlaveCount} 个", slaves.Count);
            }
            else if (splitEnabled && slaves.Count == 0)
            {
                Log.Warning("ReadWriteSplitting:Enabled=true 但未配置 ConnectionStrings:Slaves，读写分离未生效");
            }

            var db = new SqlSugarScope(config);
            var sqlLog = Log.ForContext("SourceContext", "SqlSugar");

            // ------------------------------------------------------------------
            // 多租户全局过滤：实现 ITenantEntity 的实体自动按当前租户过滤
            // 规则：当前租户为 0（平台层/超管）不过滤；租户用户可见「平台公共数据(TenantId=0) + 本租户数据」。
            // 租户上下文由 TenantMiddleware 从请求头 / JWT 声明解析。
            // ------------------------------------------------------------------
            if (configuration.GetValue<bool>("MultiTenant:Enabled", true))
            {
                db.QueryFilter.AddTableFilter<TaoAdmin.Common.Entities.ITenantEntity>(e =>
                    TaoAdmin.Common.MultiTenancy.TenantContext.CurrentId == 0
                    || e.TenantId == 0
                    || e.TenantId == TaoAdmin.Common.MultiTenancy.TenantContext.CurrentId);
            }

            // 写入自动补租户：新增租户实体时，TenantId 缺省为 0 则自动填当前租户
            db.Aop.DataExecuting = (value, helper) =>
            {
                Log.Information("DataExecuting: op={Op} entity={Entity}", helper.OperationType, helper.EntityValue?.GetType().Name);
                if (helper.OperationType == DataFilterType.InsertByObject
                    && helper.EntityValue is TaoAdmin.Common.Entities.ITenantEntity tenantEntity
                    && tenantEntity.TenantId == 0)
                {
                    Log.Information("AOP补租户: {Entity} → {TenantId}", helper.EntityValue.GetType().Name, TaoAdmin.Common.MultiTenancy.TenantContext.CurrentId);
                    tenantEntity.TenantId = TaoAdmin.Common.MultiTenancy.TenantContext.CurrentId;
                }
            };

            // AOP：SQL 执行前记录语句与参数，写入 Serilog（Console + Elasticsearch）
            db.Aop.OnLogExecuting = (sql, pars) =>
            {
                var paramText = pars == null ? string.Empty
                    : string.Join(", ", pars.Select(p => $"{p.ParameterName}={p.Value}"));
                sqlLog.Information("执行SQL: {Sql} | 参数: {Params}", sql, paramText);
            };

            // AOP：慢 SQL 告警（超过 1 秒）
            db.Aop.OnLogExecuted = (sql, pars) =>
            {
                if (db.Ado.SqlExecutionTime > TimeSpan.FromSeconds(1))
                {
                    sqlLog.Warning("慢SQL({Elapsed}ms): {Sql}", db.Ado.SqlExecutionTime.TotalMilliseconds, sql);
                }
            };

            // AOP：SQL 执行异常记录
            db.Aop.OnError = ex =>
            {
                sqlLog.Error(ex, "SQL执行异常: {Sql}", ex.Sql);
            };

            return db;
        });

        return services;
    }

    /// <summary>
    /// CodeFirst 钩子：启动时按实体类型建表/更新表结构，然后执行种子数据回调。
    /// 各服务在 Program.cs 启动阶段调用，例如：
    /// <code>
    /// db.RunCodeFirst(typeof(SysUser), typeof(SysRole), seedAction);
    /// </code>
    /// </summary>
    public static void RunCodeFirst(this ISqlSugarClient db, Action<ISqlSugarClient> seedAction, params Type[] entityTypes)
    {
        // 按实体特性自动创建或增量更新表结构（MySQL，DDL 固定走主库）
        db.CodeFirst.InitTables(entityTypes);
        seedAction?.Invoke(db);
    }
}
