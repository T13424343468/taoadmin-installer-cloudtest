using TaoAdmin.Application.Services;
using TaoAdmin.Common.Cache;
using Nacos;
using Nacos.AspNetCore.V2;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.TaskService.Entities;
using TaoAdmin.TaskService.Jobs;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using SqlSugar;
using Serilog;
using TaoAdmin.Infrastructure.Persistence;
using TaoAdmin.Infrastructure.Web;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ==================== Serilog：控制台 + JSON 文件（ES 可选） ====================
var esUrl = builder.Configuration["Elasticsearch:Uri"];
var logCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "taoadmin-task")
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(),
        Path.Combine(builder.Configuration["Monitor:LogDir"] ?? "logs", "task.jsonl"),
        rollingInterval: RollingInterval.Infinite, shared: true);
if (!string.IsNullOrEmpty(esUrl))
    logCfg.WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(esUrl))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "taoadmin-task-{0:yyyy.MM.dd}",
        EmitEventFailure = Serilog.Sinks.Elasticsearch.EmitEventFailureHandling.WriteToSelfLog
    });
Log.Logger = logCfg.CreateLogger();
builder.Host.UseSerilog();

// ==================== Nacos（可选开关） ====================
if (builder.Configuration.GetValue<bool>("Nacos:Enabled"))
{
    builder.Configuration.AddNacosV2Configuration(builder.Configuration.GetSection("NacosConfig"));
    builder.Services.AddNacosAspNet(builder.Configuration, "Nacos");
}

// ==================== SqlSugar + Redis + 雪花ID ====================
builder.Services.AddSqlSugar(builder.Configuration);
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<RedisHelper>();
// 资源监控（②）：本机指标采集器
builder.Services.AddSingleton<TaoAdmin.Common.Monitoring.ResourceCollector>(_ =>
    new TaoAdmin.Common.Monitoring.ResourceCollector(
        "taoadmin-task",
        new TaoAdmin.Common.Monitoring.ProcessResourceSource()));
builder.Services.AddSingleton(_ => new SnowflakeIdGenerator(
    builder.Configuration.GetValue<long>("Snowflake:DatacenterId", 1),
    builder.Configuration.GetValue<long>("Snowflake:WorkerId", 2)));

// ==================== JWT（服务内二次校验兜底） ====================
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SecretKey"] ?? "")),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, TaoAdmin.Common.Auth.PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, TaoAdmin.Common.Auth.PermissionAuthorizationHandler>();

// ==================== Quartz ====================
builder.Services.AddSingleton<IJobFactory, JobFactory>();
builder.Services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();
builder.Services.AddHostedService<SchedulerHostedService>();
builder.Services.AddScoped<HeartbeatJob>();
// 告警通知接线：SMTP 真实发送 + 钉钉 Webhook 真实发送 + 执行历史记录（失败按规则触发告警）
builder.Services.AddSingleton<DingTalkSender>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<AlertNotifier>();
builder.Services.AddSingleton<JobExecutionRecorder>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.WriteAsString);
// 接口文档：官方内置 OpenAPI + Scalar UI
builder.Services.AddOpenApi("v1", o =>
    o.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info = new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "TaoAdmin 任务服务 API（定时任务 CRUD / 执行控制）",
            Version = "v1"
        };
        return Task.CompletedTask;
    }));

var app = builder.Build();

app.UseAuthentication();
app.MapGet("/health", () => Results.Ok(new { status = "UP", service = "taoadmin-task" }));
// 资源指标端点（②）：本进程 CPU/内存/磁盘/网络快照，供网关聚合与监控中心采样
app.MapGet("/metrics/resources", (TaoAdmin.Common.Monitoring.ResourceCollector collector) =>
    Results.Ok(collector.Collect()));
app.UseAuthorization();
// 接口鉴权：按 SysApi 权限码校验（超管放行）
app.UseMiddleware<TaoAdmin.Infrastructure.Web.ApiAuthMiddleware>();
app.MapOpenApi(); // /openapi/v1.json
// ⑥ 文档访问限制：默认仅本机可看，站点配置 DocsAccess:Mode 可放宽为登录后可看（须在文档端点之前）
app.UseMiddleware<TaoAdmin.Common.Web.ApiDocsAccessMiddleware>();
app.MapScalarApiReference(c => c.WithTitle("TaoAdmin 任务服务")); // /scalar 可视化文档
app.MapControllers();

// ==================== 启动：建表 + 示例任务 ====================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
    db.CodeFirst.InitTables(typeof(SysJob), typeof(SysJobLog),
        typeof(TaoAdmin.Domain.Entities.SysAlertRule), typeof(TaoAdmin.Domain.Entities.SysNotifyLog));
    // 库表与种子由 IdentityService 统一初始化；本服务只保证接口扫描可用，失败不阻塞启动
    try
    {
        db.CodeFirst.InitTables(typeof(TaoAdmin.Domain.Entities.SysApi));
        ApiEndpointScanner.SyncApis(db, scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>(), typeof(Program).Assembly);
    }
    catch (Exception ex)
    {
        Log.Logger.Warning(ex, "TaskService 接口扫描初始化失败（IdentityService 初始化中，下次启动补登）");
    }
    if (db.Queryable<SysJob>().Count() == 0)
    {
        await db.Insertable(new SysJob
        {
            Id = scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>().NextId(),
            Name = "heartbeatJob",
            Group = "default",
            Cron = "0/30 * * * * ?",
            Handler = "heartbeatJob",
            Description = "示例任务：每 30 秒向 Redis 写入服务心跳",
            Status = "RUNNING",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        }).ExecuteCommandAsync();
    }
}

Log.Logger.Information("TaoAdmin.TaskService 启动，端口 5002");
app.Run();
