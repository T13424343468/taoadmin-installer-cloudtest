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

    public AuthController(AuthAppService auth) => _auth = auth;

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
