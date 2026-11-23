using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Cache;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Messaging;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Common.Storage;
using TaoAdmin.Domain.Repositories;
using TaoAdmin.IdentityService.Handlers;
using TaoAdmin.IdentityService.Services;
using TaoAdmin.Infrastructure.Persistence;
using TaoAdmin.Infrastructure.Repositories;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Nacos;
using Nacos.AspNetCore.V2;
using TaoAdmin.Domain;
using Serilog;
using SqlSugar;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ==================== Serilog：控制台 + JSON 文件（ES 可选） ====================
var esUrl = builder.Configuration["Elasticsearch:Uri"];
var logCfg = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "taoadmin-identity")
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(),
        Path.Combine(builder.Configuration["Monitor:LogDir"] ?? "logs", "identity.jsonl"),
        rollingInterval: RollingInterval.Infinite, shared: true);
if (!string.IsNullOrEmpty(esUrl))
    logCfg.WriteTo.Elasticsearch(new Serilog.Sinks.Elasticsearch.ElasticsearchSinkOptions(new Uri(esUrl))
    {
        AutoRegisterTemplate = true,
        IndexFormat = "taoadmin-identity-{0:yyyy.MM.dd}",
        EmitEventFailure = Serilog.Sinks.Elasticsearch.EmitEventFailureHandling.WriteToSelfLog
    });
Log.Logger = logCfg.CreateLogger();
builder.Host.UseSerilog();

// ==================== Nacos（可选开关，本地/沙箱可关闭） ====================
if (builder.Configuration.GetValue<bool>("Nacos:Enabled"))
{
    builder.Configuration.AddNacosV2Configuration(builder.Configuration.GetSection("NacosConfig"));
    builder.Services.AddNacosAspNet(builder.Configuration, "Nacos");
}

// ==================== SqlSugar（MySQL） ====================
builder.Services.AddSqlSugar(builder.Configuration);

// ==================== Redis ====================
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ => StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<RedisHelper>();

// ==================== 雪花ID ====================
builder.Services.AddSingleton(_ => new SnowflakeIdGenerator(
    builder.Configuration.GetValue<long>("Snowflake:DatacenterId", 1),
    builder.Configuration.GetValue<long>("Snowflake:WorkerId", 1)));

// ==================== JWT ====================
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<TaoAdmin.Common.Storage.FileStorageOptions>(builder.Configuration.GetSection("FileStorage"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value);
builder.Services.AddSingleton<JwtTokenService>();
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// ==================== 在线会话（在线用户名单 + 强制下线账本） ====================
builder.Services.AddSingleton(sp => new OnlineSessionStore(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<JwtOptions>>().Value.ExpireMinutes));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // 网关已做首次校验，服务内二次校验兜底（防绕过网关直连）
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

// ==================== 权限码策略授权 ====================
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

// ==================== DDD 依赖装配：应用服务 + 仓储实现 ====================
builder.Services.AddScoped<PermissionAppService>();
builder.Services.AddScoped<AuthAppService>();
builder.Services.AddScoped<UserAppService>();
builder.Services.AddScoped<ProfileAppService>();
builder.Services.AddScoped<RoleAppService>();
builder.Services.AddScoped<MenuAppService>();
builder.Services.AddScoped<ApiAppService>();
builder.Services.AddSingleton<MonitorAppService>();
// 资源监控（②）：本机指标采集 + 后台定时采样 + 历史趋势 + 阈值告警事件
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TaoAdmin.Common.Monitoring.ResourceCollector>(_ =>
    new TaoAdmin.Common.Monitoring.ResourceCollector(
        "taoadmin-identity",
        new TaoAdmin.Common.Monitoring.ProcessResourceSource()));
builder.Services.AddSingleton<TaoAdmin.Common.Monitoring.ResourceHistoryStore>();
builder.Services.AddSingleton<TaoAdmin.Common.Alerting.InMemoryAlertEventPublisher>();
builder.Services.AddSingleton<TaoAdmin.Common.Alerting.IAlertEventPublisher>(sp =>
    sp.GetRequiredService<TaoAdmin.Common.Alerting.InMemoryAlertEventPublisher>());
builder.Services.AddHostedService<ResourceMonitorService>();
// 扩展功能：工作流 / 多租户 / 站内消息 / 数据字典 / 操作日志 / 站点参数
builder.Services.AddMemoryCache();
builder.Services.AddScoped<WorkflowAppService>();
builder.Services.AddScoped<TenantAppService>();
builder.Services.AddScoped<MessageAppService>();
builder.Services.AddScoped<DictAppService>();
builder.Services.AddScoped<OperationLogAppService>();
builder.Services.AddScoped<SiteConfigAppService>();
builder.Services.AddScoped<AnnouncementAppService>();
builder.Services.AddScoped<CodeGenAppService>();
builder.Services.AddScoped<UserExcelAppService>();
// 资源监控阈值（②）：存站点配置键，监控页可改
builder.Services.AddScoped<ResourceThresholdProvider>();
// 防护审计：登录安全（验证码/锁定策略读取口）——AuthAppService 登录链依赖 ILoginSecurityProvider
builder.Services.AddScoped<LoginSecurityAppService>();
builder.Services.AddScoped<ILoginSecurityProvider>(sp => sp.GetRequiredService<LoginSecurityAppService>());
builder.Services.AddSingleton<TaoAdmin.Common.Security.CaptchaService>();
// 登录锁定账本（⑭）：失败计数/锁定状态/剩余时间全存 Redis（带过期），重启不清零；
// Redis 不可用时降级放行（fail-open）并记 Warning，恢复后账本继续生效
builder.Services.AddSingleton<TaoAdmin.Common.Auth.IRedisLockoutDatabase>(sp =>
    new TaoAdmin.Common.Auth.RedisLockoutDatabaseAdapter(sp.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>()));
builder.Services.AddSingleton<TaoAdmin.Common.Auth.ILoginLockoutStore>(sp =>
    new TaoAdmin.Common.Auth.RedisLoginLockoutStore(
        sp.GetRequiredService<TaoAdmin.Common.Auth.IRedisLockoutDatabase>(),
        sp.GetService<Microsoft.Extensions.Logging.ILogger<TaoAdmin.Common.Auth.RedisLoginLockoutStore>>()));
builder.Services.AddSingleton<TaoAdmin.Application.Services.AuditCleanupService>();
builder.Services.AddSingleton<TaoAdmin.Application.Services.AuditTrail>();
builder.Services.AddScoped<NotifyChannelAppService>();
// 告警通知接线：SMTP 真实发送 + 钉钉 Webhook 真实发送 + 规则化告警（配置运行时从库读取）
builder.Services.AddSingleton<DingTalkSender>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<AlertNotifier>();
builder.Services.AddScoped<AlertRuleAppService>();
builder.Services.AddSingleton<ServiceDownAlertEvaluator>(sp =>
{
    var notifier = sp.GetRequiredService<AlertNotifier>();
    return new ServiceDownAlertEvaluator((eventType, title, content, count) =>
        notifier.NotifyAsync(eventType, title, content, count));
});
builder.Services.AddHostedService<TaoAdmin.IdentityService.Monitoring.ServiceHealthMonitorService>();
// 文件中心：本地盘存储默认；经 FileStorage:Provider=minio 切 MinIO 对象存储（数据不出内网）
builder.Services.AddScoped<FileAppService>();
builder.Services.AddScoped<TaoAdmin.Common.Storage.IFileStorageProvider>(sp =>
{
    var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FileStorageOptions>>().Value;
    return cfg.Provider.Equals("minio", StringComparison.OrdinalIgnoreCase)
        ? new MinioFileStorageProvider(cfg.Minio.Endpoint, cfg.Minio.AccessKey, cfg.Minio.SecretKey, cfg.Minio.Bucket)
        : new LocalFileStorageProvider(cfg.Root, sp.GetRequiredService<SnowflakeIdGenerator>().NextId);
});
// 密码策略：应用服务本身即策略读取口（改密/建人/登录注入同一实现）
builder.Services.AddScoped<PasswordResetAppService>();
builder.Services.AddScoped<PasswordPolicyAppService>();
builder.Services.AddScoped<IPasswordPolicyProvider>(sp => sp.GetRequiredService<PasswordPolicyAppService>());
// 行政区划（⑪地址库）：只读查询 + 后台更新（在线抓取民政部国家地名信息库，内置数据包兜底）
builder.Services.AddScoped<RegionAppService>();
builder.Services.AddHttpClient(TaoAdmin.Infrastructure.Persistence.McaRegionDataSource.ClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddSingleton<TaoAdmin.Infrastructure.Persistence.McaRegionDataSource>(sp =>
    new TaoAdmin.Infrastructure.Persistence.McaRegionDataSource(
        sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient(TaoAdmin.Infrastructure.Persistence.McaRegionDataSource.ClientName),
        sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TaoAdmin.Infrastructure.Persistence.McaRegionDataSource>>()));
builder.Services.AddSingleton<TaoAdmin.Application.Services.IRegionDataSource>(sp => sp.GetRequiredService<TaoAdmin.Infrastructure.Persistence.McaRegionDataSource>());
builder.Services.AddSingleton<TaoAdmin.Application.Services.IRegionPackageProvider, TaoAdmin.Infrastructure.Persistence.BuiltinRegionPackageProvider>();
builder.Services.AddSingleton<TaoAdmin.Application.Services.RegionWriteService>();
builder.Services.AddSingleton<TaoAdmin.Application.Services.RegionUpdateService>();
// 接口文档：官方内置 OpenAPI + Scalar UI
builder.Services.AddOpenApi("v1", o =>
    o.AddDocumentTransformer((doc, ctx, ct) =>
    {
        doc.Info = new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "TaoAdmin 权限服务 API（认证/用户/角色/菜单/接口/租户/工作流/消息/字典/日志）",
            Version = "v1"
        };
        return Task.CompletedTask;
    }));
// 消息队列：按配置选择内存/RabbitMQ 实现，后台泵自动订阅所有 IMessageHandler
builder.Services.AddMessageQueue(builder.Configuration);
builder.Services.AddSingleton<TaoAdmin.Common.Messaging.IMessageHandler, UserCreatedMessageHandler>();
builder.Services.AddSingleton<TaoAdmin.Common.Messaging.IMessageHandler, DemoTopicMessageHandler>();
TaoAdmin.Utilities.HttpHelperExtensions.AddHttpHelper(builder.Services);
builder.Services.AddScoped<ISysUserRepository, SysUserRepository>();
builder.Services.AddScoped<ISysRoleRepository, SysRoleRepository>();
builder.Services.AddScoped<ISysMenuRepository, SysMenuRepository>();
builder.Services.AddScoped<ISysApiRepository, SysApiRepository>();
// 接口幂等性：全局过滤器，仅对标注 [Idempotent] 的接口生效
builder.Services.AddSingleton<TaoAdmin.Common.Idempotency.IdempotencyStore>();
builder.Services.AddControllers(options =>
{
    options.Filters.Add<TaoAdmin.Common.Idempotency.IdempotencyFilter>();
})
    .AddJsonOptions(o =>
    {
        // 长整型（雪花ID）序列化为字符串，避免前端精度丢失
        o.JsonSerializerOptions.NumberHandling = JsonNumberHandling.WriteAsString;
    });

var app = builder.Build();

// ==================== 中间件管道 ====================
app.UseSerilogRequestLogging();
app.MapGet("/health", () => Results.Ok(new { status = "UP", service = "taoadmin-identity" }));
// 资源指标端点（②）：本进程 CPU/内存/磁盘/网络快照，供网关聚合与监控中心采样
app.MapGet("/metrics/resources", (TaoAdmin.Common.Monitoring.ResourceCollector collector) =>
    Results.Ok(collector.Collect()));
app.UseAuthentication();
// 会话吊销校验：强制下线后令牌即刻失效（依赖认证结果，须在 UseAuthentication 之后）
app.UseMiddleware<TaoAdmin.IdentityService.Middleware.SessionRevocationMiddleware>();
app.UseAuthorization();
// 接口级授权（增强层）：按 SysApi 登记（路径+HTTP 方法）叠加校验角色-接口授权，
// 未登记接口/匿名接口/超管直接放行，与权限码级校验两道都过才放行
app.UseMiddleware<TaoAdmin.Infrastructure.Web.ApiAuthMiddleware>();
// 多租户：解析当前租户（X-Tenant-Id 头 / JWT tenant_id 声明），供全局数据过滤使用
app.UseMiddleware<TaoAdmin.Common.MultiTenancy.TenantMiddleware>();
// 操作日志：记录写操作与登录审计（异步落库，不阻塞请求）
app.UseMiddleware<TaoAdmin.IdentityService.Middleware.OperationLogMiddleware>();
app.MapOpenApi(); // /openapi/v1.json
// ⑥ 文档访问限制：默认仅本机可看，站点配置 DocsAccess:Mode 可放宽为登录后可看（须在文档端点之前）
app.UseMiddleware<TaoAdmin.Common.Web.ApiDocsAccessMiddleware>();
app.MapScalarApiReference(c => c.WithTitle("TaoAdmin 权限服务")); // /scalar 可视化文档
app.MapControllers();

// 领域异常 -> 统一返回
app.UseExceptionHandler("/error");
app.Map("/error", (HttpContext ctx) =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var code = ex is DomainException de ? de.Code : 500;
    ctx.Items["BizFail"] = code >= 400; // 供操作日志中间件识别业务失败（如密码错误）
    ctx.Response.StatusCode = 200;
    ctx.Response.WriteAsJsonAsync(new ApiResult<object>
    {
        Code = code,
        Success = false,
        Message = ex?.Message ?? "服务器内部错误",
        TraceId = ctx.TraceIdentifier
    });
});

// ==================== 启动：建表 + 种子 ====================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
    var snowflake = scope.ServiceProvider.GetRequiredService<SnowflakeIdGenerator>();
    DbInitializer.Initialize(db, snowflake, typeof(Program).Assembly);
    Log.Logger.Information("IdentityService 就绪（默认账号 admin / Admin@123）");
}

Log.Logger.Information("TaoAdmin.IdentityService 启动，端口 {Port}", builder.Configuration.GetValue<int>("Port", 5001));
app.Run();
