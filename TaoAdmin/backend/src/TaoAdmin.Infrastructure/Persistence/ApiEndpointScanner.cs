using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Infrastructure.Persistence;

/// <summary>
/// 扫描到的接口端点描述（一次反射扫描的中间结果）。
/// </summary>
public record ApiEndpointInfo(string Name, string Path, string HttpMethod, string PermsCode);

/// <summary>
/// 接口资源自动扫描登记器：启动时反射扫描程序集中所有控制器动作，
/// 把带 [Permission] 的真实接口增量同步进 SysApi 表——
/// 新接口补录、已消失的接口标记停用（Status=0），不删历史数据。
/// 扫描结果供接口管理页展示与角色-接口授权使用。
/// </summary>
public static class ApiEndpointScanner
{
    /// <summary>
    /// 增量同步接口资源：扫描 → 与库内现存记录对账 → 补录新增 / 停用消失。
    /// 幂等：重复调用只补差异。目标程序集由各服务启动时传入（自身控制器程序集），
    /// 避免基础设施层反向依赖服务层造成循环引用。
    /// </summary>
    public static void SyncApis(ISqlSugarClient db, SnowflakeIdGenerator snowflake, params Assembly[] targetAssemblies)
    {
        if (targetAssemblies.Length == 0) return;
        var now = DateTime.Now;
        var scanned = ScanEndpoints(targetAssemblies).ToList();

        // 同路径同方法视为同一接口；库内现存记录按 (Path, HttpMethod) 建索引
        var existing = db.Queryable<SysApi>().Where(a => a.IsDeleted == false).ToList()
            .GroupBy(a => Key(a.Path, a.HttpMethod))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var group in scanned.GroupBy(e => Key(e.Path, e.HttpMethod)))
        {
            var endpoint = group.First();
            if (existing.Remove(group.Key, out var row))
            {
                // 已存在：权限码或名称变化时更新（保留人工改过的状态）
                if (row.PermsCode != endpoint.PermsCode || row.Name != endpoint.Name)
                {
                    row.Name = endpoint.Name;
                    row.PermsCode = endpoint.PermsCode;
                    row.UpdatedAt = now;
                    db.Updateable(row).UpdateColumns(a => new { a.Name, a.PermsCode, a.UpdatedAt }).ExecuteCommand();
                }
            }
            else
            {
                // 新接口：补录，默认启用
                db.Insertable(new SysApi
                {
                    Id = snowflake.NextId(),
                    Name = endpoint.Name,
                    Path = endpoint.Path,
                    HttpMethod = endpoint.HttpMethod,
                    PermsCode = endpoint.PermsCode,
                    Status = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                }).ExecuteCommand();
            }
        }

        // 库内仍有、但扫描不到的接口：代码已下线，标记停用（不删历史数据）
        foreach (var stale in existing.Values)
        {
            if (stale.Status != 0)
            {
                stale.Status = 0;
                stale.UpdatedAt = now;
                db.Updateable(stale).UpdateColumns(a => new { a.Status, a.UpdatedAt }).ExecuteCommand();
            }
        }
    }

    /// <summary>反射扫描全部控制器动作，产出带 [Permission] 的接口端点描述。</summary>
    internal static IEnumerable<ApiEndpointInfo> ScanEndpoints(Assembly[] targetAssemblies)
    {
        foreach (var assembly in targetAssemblies)
        {
            var controllers = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t));
            foreach (var controller in controllers)
            {
                var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template?.Trim('/') ?? string.Empty;
                foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(m => m.GetCustomAttribute<PermissionAttribute>() != null))
                {
                    var (httpMethod, template) = HttpInfo(method);
                    if (httpMethod == null) continue;
                    var path = "/" + JoinPath(controllerRoute, template);
                    var perm = method.GetCustomAttribute<PermissionAttribute>()!;
                    yield return new ApiEndpointInfo(
                        Name: ActionName(controller.Name, method.Name),
                        Path: path,
                        HttpMethod: httpMethod,
                        PermsCode: perm.PermsCode);
                }
            }
        }
    }

    /// <summary>取动作的 HTTP 方法与路由模板（如 ("GET", "{id}/roles")）。</summary>
    private static (string? Method, string Template) HttpInfo(MethodInfo method)
    {
        if (method.GetCustomAttribute<HttpGetAttribute>() is var get && get != null) return ("GET", get.Template ?? "");
        if (method.GetCustomAttribute<HttpPostAttribute>() is var post && post != null) return ("POST", post.Template ?? "");
        if (method.GetCustomAttribute<HttpPutAttribute>() is var put && put != null) return ("PUT", put.Template ?? "");
        if (method.GetCustomAttribute<HttpDeleteAttribute>() is var del && del != null) return ("DELETE", del.Template ?? "");
        if (method.GetCustomAttribute<HttpPatchAttribute>() is var patch && patch != null) return ("PATCH", patch.Template ?? "");
        if (method.GetCustomAttribute<AcceptVerbsAttribute>() is { } verbs && verbs.HttpMethods.Any())
            return (verbs.HttpMethods.First().ToUpperInvariant(), "");
        return (null, "");
    }

    /// <summary>控制器路由 + 动作模板拼完整路径（兼容空段）。</summary>
    private static string JoinPath(string controllerRoute, string actionTemplate)
    {
        if (string.IsNullOrEmpty(actionTemplate)) return controllerRoute;
        return string.IsNullOrEmpty(controllerRoute) ? actionTemplate : $"{controllerRoute}/{actionTemplate}";
    }

    /// <summary>接口名称：控制器名去 Controller 后缀 + 动作名，如 "Users-Page"。</summary>
    private static string ActionName(string controllerName, string actionName)
        => $"{controllerName.Replace("Controller", string.Empty)}-{actionName}";

    private static string Key(string path, string httpMethod) => $"{httpMethod.ToUpperInvariant()} {path}";
}
