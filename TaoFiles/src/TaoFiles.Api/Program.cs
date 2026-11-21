using TaoFiles.Api.Controllers;
using TaoFiles.Api.Entities;
using TaoFiles.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "文件中心 API",
        Version = "v1",
        Description = "独立文件管理服务：上传（MD5 去重/秒传）、列表、删除、下载。业务系统只需保存返回的相对路径。"
    });
});

// SqlSugar + CodeFirst
var conn = builder.Configuration["ConnectionStrings:MySql"]
    ?? "server=127.0.0.1;port=3306;database=tao_files;uid=root;pwd=Micro@2026;charset=utf8mb4;";
builder.Services.AddSingleton<ISqlSugarClient>(_ =>
{
    var db = new SqlSugarClient(new ConnectionConfig
    {
        ConnectionString = conn,
        DbType = DbType.MySql,
        IsAutoCloseConnection = true,
        InitKeyType = InitKeyType.Attribute
    });
    db.CodeFirst.InitTables<SysFile>();
    return db;
});

// 存储实现（本地磁盘，可替换为 MinIO/OSS）
builder.Services.AddSingleton<IFileStorageProvider>(sp =>
    new LocalFileStorageProvider(
        builder.Configuration["FileStorage:Root"] ?? Path.Combine(builder.Environment.ContentRootPath, "uploads"),
        () => Snowflake.NextId()));
builder.Services.AddScoped<FileAppService>();

// 跨域：允许管理后台等前端页面直接调用（如需收紧可改为白名单 origins）
const string CorsPolicy = "fc-cors";
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors(CorsPolicy);

// 统一异常处理：业务异常转 400 JSON，其余转 500
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DomainException ex)
    {
        ctx.Response.StatusCode = ex.Code;
        await ctx.Response.WriteAsJsonAsync(new { code = ex.Code, msg = ex.Message });
    }
    catch
    {
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(new { code = 500, msg = "服务器内部错误" });
    }
});

// 可选 API Key：配置 TaoFiles:ApiKey 后，所有 /api 接口要求 X-Api-Key 请求头
var apiKey = app.Configuration["TaoFiles:ApiKey"];
if (!string.IsNullOrEmpty(apiKey))
{
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api") &&
            ctx.Request.Headers["X-Api-Key"] != apiKey)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { code = 401, msg = "API Key 无效" });
            return;
        }
        await next();
    });
}

app.UseDefaultFiles();   // / 指向管理页面
app.UseStaticFiles();
// /uploads/** 映射到存储根目录：相对路径 + 域名即可直接访问文件
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.GetFullPath(app.Configuration["FileStorage:Root"] ?? "uploads")),
    RequestPath = "/uploads",
    ServeUnknownFileTypes = true
});
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "文件中心 API v1");
    c.DocumentTitle = "文件中心";
});

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "UP", service = "file-center" }));

app.Run();

/// <summary>暴露给集成测试</summary>
public partial class Program { }
