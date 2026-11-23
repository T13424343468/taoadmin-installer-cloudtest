using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Auth;
using TaoAdmin.Common.Http;
using TaoAdmin.Common.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TaoAdmin.IdentityService.Controllers;

/// <summary>认证入口：登录 / 当前用户 / 退出。业务在 AuthAppService。</summary>
[Route("auth")]
public class AuthController : BaseApiController
{
    private readonly AuthAppService _auth;

    private readonly PasswordResetAppService _reset;

    public AuthController(AuthAppService auth, PasswordResetAppService reset)
    {
        _auth = auth; _reset = reset;
    }

    /// <summary>账号密码登录（登录来源登记进在线会话，供在线名单与强制下线使用）</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ApiResult<LoginResult>> Login([FromBody] LoginRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var userAgent = Request.Headers.UserAgent.ToString();
        var (token, _) = await _auth.LoginAsync(request, ip, userAgent);
        return ApiOk(new LoginResult { token = token }, "登录成功");
    }

    /// <summary>当前登录用户信息（角色 + 权限码 + 菜单树）</summary>
    [HttpGet("userinfo")]
    public async Task<ApiResult<CurrentUserDto>> CurrentUser()
        => ApiOk(await _auth.GetCurrentUserAsync(User.GetUserId()));

    /// <summary>锁屏解锁：复核当前账号登录密码（已登录会话内调用，不重发令牌、不走验证码）</summary>
    [HttpPost("verify-password")]
    public async Task<ApiResult<object>> VerifyPassword([FromBody] VerifyPasswordRequest request)
    {
        await _auth.VerifyPasswordAsync(User.GetUserId(), request.password);
        return ApiOk("密码正确");
    }

    /// <summary>忘记密码：申请重置令牌（发到账号绑定邮箱；统一提示，不泄露账号是否存在）</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<ApiResult<object>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _reset.RequestResetAsync(request.username);
        return ApiOk("如账号存在且已绑定邮箱，重置令牌已发送，请查收邮件");
    }

    /// <summary>重置密码：凭邮箱收到的令牌设置新密码（令牌一次性、15 分钟内有效）</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<ApiResult<object>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await _reset.ResetAsync(request.username, request.token, request.newPassword);
        return ApiOk("密码重置成功，请使用新密码登录");
    }

    /// <summary>退出登录（吊销当前会话并清理刷新令牌）</summary>
    [HttpPost("logout")]
    public async Task<ApiResult<object>> Logout()
    {
        await _auth.LogoutAsync(User.GetUserId(), User.FindFirst("jti")?.Value);
        return ApiOk("退出成功");
    }
}

/// <summary>用户管理入口。业务在 UserAppService。</summary>
[Route("users")]
public class UserController : BaseApiController
{
    private readonly UserAppService _svc;
    private readonly UserExcelAppService _excel;

    public UserController(UserAppService svc, UserExcelAppService excel)
    {
        _svc = svc; _excel = excel;
    }

    /// <summary>分页查询用户</summary>
    [HttpGet]
    [Permission("sys:user:list")]
    public async Task<ApiResult<Common.Http.PageResult<UserItemDto>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, keyword));

    /// <summary>新增/编辑用户（含角色分配）</summary>
    [HttpPost]
    [HttpPut("{id?}")]
    [Permission("sys:user:add")]
    public async Task<ApiResult<object>> Save([FromBody] UserSaveRequest req)
    {
        await _svc.SaveAsync(req);
        return ApiOk(string.IsNullOrEmpty(req.id) ? "新增成功" : "修改成功");
    }

    /// <summary>删除用户</summary>
    [HttpDelete("{id}")]
    [Permission("sys:user:delete")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _svc.DeleteAsync(long.Parse(id), User.GetUserId());
        return ApiOk("删除成功");
    }

    /// <summary>批量删除用户（自动跳过当前登录账号，返回 skipped 跳过数）</summary>
    [HttpPost("batch-delete")]
    [Permission("sys:user:delete")]
    public async Task<ApiResult<object>> BatchDelete([FromBody] BatchIdsRequest req)
    {
        var skipped = await _svc.BatchDeleteAsync(req.Ids, User.GetUserId());
        return ApiOk<object>(new { deleted = req.Ids.Distinct().Count() - skipped, skipped }, "批量删除完成");
    }

    /// <summary>批量启用/停用用户（自动跳过当前登录账号，返回 skipped 跳过数与失败明细）</summary>
    [HttpPost("batch-status")]
    [Permission("sys:user:edit")]
    public async Task<ApiResult<object>> BatchStatus([FromBody] BatchStatusRequest req)
    {
        var (skipped, failed) = await _svc.BatchSetStatusAsync(req.Ids, req.status, User.GetUserId());
        return ApiOk<object>(new { updated = req.Ids.Distinct().Count() - skipped - failed.Count, skipped, failed }, "批量操作完成");
    }

    /// <summary>重置密码</summary>
    [HttpPut("{id}/password")]
    [Permission("sys:user:resetPwd")]
    public async Task<ApiResult<object>> ResetPassword(string id, [FromBody] Dictionary<string, string> body)
    {
        body.TryGetValue("password", out var password);
        await _svc.ResetPasswordAsync(long.Parse(id), password ?? string.Empty);
        return ApiOk("密码已重置");
    }

    /// <summary>分配角色</summary>
    [HttpPut("{id}/roles")]
    [Permission("sys:user:assignRole")]
    public async Task<ApiResult<object>> AssignRoles(string id, [FromBody] Dictionary<string, List<string>> body)
    {
        body.TryGetValue("roleIds", out var roleIds);
        await _svc.AssignRolesAsync(long.Parse(id), roleIds ?? new List<string>());
        return ApiOk("角色分配成功");
    }

    /// <summary>导出用户为 xlsx（密码列不导出；keyword 与分页查询口径一致）</summary>
    [HttpGet("export")]
    [Permission("sys:user:list")]
    public async Task<IActionResult> Export([FromQuery] string? keyword = null)
    {
        var bytes = await _excel.ExportAsync(keyword);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"用户导出-{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    /// <summary>下载用户导入模板</summary>
    [HttpGet("import-template")]
    [Permission("sys:user:list")]
    public async Task<IActionResult> ImportTemplate()
    {
        var bytes = await _excel.BuildTemplateAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "用户导入模板.xlsx");
    }

    /// <summary>导入用户（xlsx；逐行校验，错误行返回明细不中断）</summary>
    [HttpPost("import")]
    [Permission("sys:user:add")]
    public async Task<ApiResult<UserImportResultDto>> Import(IFormFile file)
    {
        if (file is null || file.Length == 0)
            throw new TaoAdmin.Domain.DomainException("请选择要导入的表格文件", 400);
        if (file.Length > 5 * 1024 * 1024)
            throw new TaoAdmin.Domain.DomainException("导入文件不超过 5MB", 400);
        await using var stream = file.OpenReadStream();
        return ApiOk(await _excel.ImportAsync(stream, User.GetUserId()));
    }
}

/// <summary>角色管理入口。业务在 RoleAppService。</summary>
[Route("roles")]
public class RoleController : BaseApiController
{
    private readonly RoleAppService _svc;

    public RoleController(RoleAppService svc) => _svc = svc;

    /// <summary>分页查询角色</summary>
    [HttpGet]
    [Permission("sys:role:list")]
    public async Task<ApiResult<Common.Http.PageResult<RoleItemDto>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
    {
        var page = await _svc.ListAsync();
        if (!string.IsNullOrEmpty(keyword))
            page = page.Where(r => r.name.Contains(keyword) || r.code.Contains(keyword)).ToList();
        return ApiOk(new Common.Http.PageResult<RoleItemDto>(page.Count, pageIndex, pageSize,
            page.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList()));
    }

    /// <summary>全部角色（下拉与分配用）</summary>
    [HttpGet("all")]
    [Permission("sys:role:list")]
    public async Task<ApiResult<List<RoleItemDto>>> All() => ApiOk(await _svc.ListAsync());

    /// <summary>新增/编辑角色（含权限分配）</summary>
    [HttpPost]
    [HttpPut("{id?}")]
    [Permission("sys:role:add")]
    public async Task<ApiResult<object>> Save([FromBody] RoleSaveRequest req)
    {
        await _svc.SaveAsync(req);
        return ApiOk(string.IsNullOrEmpty(req.id) ? "新增成功" : "修改成功");
    }

    /// <summary>删除角色</summary>
    [HttpDelete("{id}")]
    [Permission("sys:role:delete")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _svc.DeleteAsync(long.Parse(id));
        return ApiOk("删除成功");
    }

    /// <summary>批量删除角色（强一致：整批一个事务全成全败，任一条失败全部回滚）</summary>
    [HttpPost("batch-delete")]
    [Permission("sys:role:delete")]
    public async Task<ApiResult<object>> BatchDelete([FromBody] BatchIdsRequest req)
    {
        var deleted = await _svc.BatchDeleteAsync(req.Ids);
        return ApiOk<object>(new { deleted }, "批量删除完成");
    }

    /// <summary>角色已分配权限</summary>
    [HttpGet("{id}/permissions")]
    [Permission("sys:role:list")]
    public async Task<ApiResult<object>> GetPermissions(string id)
    {
        var (menuIds, apiIds) = await _svc.GetPermissionsAsync(long.Parse(id));
        return ApiOk<object>(new { menuIds, apiIds });
    }

    /// <summary>分配权限（菜单树 + 接口）</summary>
    [HttpPut("{id}/permissions")]
    [Permission("sys:role:assign")]
    public async Task<ApiResult<object>> AssignPermissions(string id, [FromBody] RolePermissionRequest req)
    {
        await _svc.AssignPermissionsAsync(long.Parse(id), req.menuIds, req.apiIds);
        return ApiOk("权限分配成功");
    }
}

/// <summary>菜单管理入口。业务在 MenuAppService。</summary>
[Route("menus")]
public class MenuController : BaseApiController
{
    private readonly MenuAppService _svc;

    public MenuController(MenuAppService svc) => _svc = svc;

    /// <summary>完整菜单树</summary>
    [HttpGet("tree")]
    [Permission("sys:menu:list")]
    public async Task<ApiResult<List<MenuNodeDto>>> Tree() => ApiOk(await _svc.TreeAsync());

    /// <summary>新增/编辑菜单（按钮必须带权限码）</summary>
    [HttpPost]
    [HttpPut("{id?}")]
    [Permission("sys:menu:add")]
    public async Task<ApiResult<object>> Save([FromBody] MenuSaveRequest req)
    {
        await _svc.SaveAsync(req);
        return ApiOk(string.IsNullOrEmpty(req.id) ? "新增成功" : "修改成功");
    }

    /// <summary>删除菜单（级联子节点）</summary>
    [HttpDelete("{id}")]
    [Permission("sys:menu:delete")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        var count = await _svc.DeleteAsync(long.Parse(id));
        return ApiOk($"删除成功（共 {count} 个节点）");
    }
}

/// <summary>接口资源管理入口。业务在 ApiAppService。</summary>
[Route("apis")]
public class ApiController : BaseApiController
{
    private readonly ApiAppService _svc;

    public ApiController(ApiAppService svc) => _svc = svc;

    /// <summary>分页查询接口资源</summary>
    [HttpGet]
    [Permission("sys:api:list")]
    public async Task<ApiResult<Common.Http.PageResult<ApiItemDto>>> Page(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 10, [FromQuery] string? keyword = null)
        => ApiOk(await _svc.PageAsync(pageIndex, pageSize, keyword));

    /// <summary>全部启用接口</summary>
    [HttpGet("all")]
    [Permission("sys:api:list")]
    public async Task<ApiResult<List<ApiItemDto>>> All() => ApiOk(await _svc.ListActiveAsync());

    /// <summary>新增/编辑接口资源</summary>
    [HttpPost]
    [HttpPut("{id?}")]
    [Permission("sys:api:add")]
    public async Task<ApiResult<object>> Save([FromBody] ApiSaveRequest req)
    {
        await _svc.SaveAsync(req);
        return ApiOk(string.IsNullOrEmpty(req.id) ? "新增成功" : "修改成功");
    }

    /// <summary>删除接口资源</summary>
    [HttpDelete("{id}")]
    [Permission("sys:api:delete")]
    public async Task<ApiResult<object>> Delete(string id)
    {
        await _svc.DeleteAsync(long.Parse(id));
        return ApiOk("删除成功");
    }

    /// <summary>批量删除接口资源（强一致：整批一个事务全成全败，任一条失败全部回滚）</summary>
    [HttpPost("batch-delete")]
    [Permission("sys:api:delete")]
    public async Task<ApiResult<object>> BatchDelete([FromBody] BatchIdsRequest req)
    {
        var deleted = await _svc.BatchDeleteAsync(req.Ids);
        return ApiOk<object>(new { deleted }, "批量删除完成");
    }
}

/// <summary>批量操作请求：ID 列表（各列表页批量删除共用）</summary>
public class BatchIdsRequest
{
    /// <summary>要操作的记录 ID</summary>
    public List<long> Ids { get; set; } = new();
}

/// <summary>批量状态请求：ID 列表 + 目标状态</summary>
public class BatchStatusRequest
{
    /// <summary>要操作的记录 ID</summary>
    public List<long> Ids { get; set; } = new();
    /// <summary>目标状态（1=启用 0=停用）</summary>
    public int status { get; set; }
}

/// <summary>监控中心入口：服务健康 + 日志查询。业务在 MonitorAppService。</summary>
[Route("monitor")]
public class MonitorController : BaseApiController
{
    private readonly MonitorAppService _svc;

    public MonitorController(MonitorAppService svc) => _svc = svc;

    /// <summary>服务健康列表</summary>
    [HttpGet("services")]
    [Permission("monitor:service:list")]
    public async Task<ApiResult<List<ServiceItemDto>>> Services() => ApiOk(await _svc.GetServicesAsync());

    /// <summary>服务健康详情</summary>
    [HttpGet("services/{name}")]
    [Permission("monitor:service:list")]
    public async Task<ApiResult<ServiceItemDto>> ServiceDetail(string name)
    {
        var list = await _svc.GetServicesAsync();
        var item = list.FirstOrDefault(s => s.serviceName.Contains(name)) ?? new ServiceItemDto { serviceName = name, status = "DOWN" };
        return ApiOk(item);
    }

    /// <summary>分页查询日志</summary>
    [HttpGet("logs")]
    [Permission("monitor:log:list")]
    public async Task<ApiResult<object>> Logs(
        [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? level = null, [FromQuery] string? service = null, [FromQuery] string? keyword = null)
    {
        var (total, items) = await _svc.GetLogsAsync(pageIndex, pageSize, level, service, keyword);
        return ApiOk<object>(new { items, total, pageIndex, pageSize });
    }
}

/// <summary>资源监控入口（②）：实时快照、近期趋势、阈值设置与近期告警事件。</summary>
[Route("monitor/resources")]
public class ResourceMonitorController(
    TaoAdmin.Common.Monitoring.ResourceHistoryStore history,
    TaoAdmin.Common.Alerting.InMemoryAlertEventPublisher alerts,
    ResourceThresholdProvider thresholds) : BaseApiController
{
    /// <summary>全部服务最新资源快照（含是否处于告警态由前端按阈值比对展示）</summary>
    [HttpGet("current")]
    [Permission("monitor:resource:list")]
    public async Task<ApiResult<object>> Current()
        => ApiOk<object>(new { items = history.LatestAll(), thresholds = await thresholds.GetAsync() });

    /// <summary>单服务近期趋势（minutes 窗口内的采样序列，旧→新）</summary>
    [HttpGet("history")]
    [Permission("monitor:resource:list")]
    public async Task<ApiResult<object>> History(
        [FromQuery] string serviceName, [FromQuery] int minutes = 60)
    {
        var span = minutes is < 1 or > 1440 ? 60 : minutes;
        var items = history.Since(serviceName, DateTime.Now.AddMinutes(-span));
        return ApiOk<object>(new { serviceName, minutes = span, items, thresholds = await thresholds.GetAsync() });
    }

    /// <summary>读取资源异常阈值</summary>
    [HttpGet("thresholds")]
    [Permission("monitor:resource:list")]
    public async Task<ApiResult<TaoAdmin.Common.Monitoring.ResourceThresholds>> Thresholds()
        => ApiOk(await thresholds.GetAsync());

    /// <summary>保存资源异常阈值（保存即生效，后台采样下一轮按新阈值评估）</summary>
    [HttpPut("thresholds")]
    [Permission("monitor:resource:manage")]
    public async Task<ApiResult<object>> SaveThresholds([FromBody] TaoAdmin.Common.Monitoring.ResourceThresholds req)
    {
        await thresholds.SaveAsync(req);
        return ApiOk("阈值已保存，立即生效");
    }

    /// <summary>近期资源告警事件（新→旧，最多 50 条）</summary>
    [HttpGet("alerts")]
    [Permission("monitor:resource:list")]
    public ApiResult<object> Alerts()
        => ApiOk<object>(new { items = alerts.Recent(50) });
}
