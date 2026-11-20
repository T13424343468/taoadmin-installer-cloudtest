namespace TaoAdmin.Application.Dtos;

// ============================================================
// 应用层 DTO：与前端契约严格对齐。
// 约定：所有 ID 一律以字符串传输（雪花ID超出 JS 安全整数范围）；
// 字段名与前端 interface 完全一致（小写开头）。
// ============================================================

/// <summary>登录请求（captchaId/captchaCode 为可选：安全设置要求验证码时必传）</summary>
public class LoginRequest
{
    public string username { get; set; } = string.Empty;
    public string password { get; set; } = string.Empty;
    public string? captchaId { get; set; }
    public string? captchaCode { get; set; }
}

/// <summary>登录响应</summary>
public class LoginResult
{
    public string token { get; set; } = string.Empty;
}

/// <summary>菜单树节点（类型字符串：dir/menu/button）</summary>
public class MenuNodeDto
{
    public string id { get; set; } = "0";
    public string? parentId { get; set; }
    public string name { get; set; } = string.Empty;
    public string? path { get; set; }
    public string? component { get; set; }
    public string? icon { get; set; }
    /// <summary>dir 目录 / menu 菜单 / button 按钮</summary>
    public string type { get; set; } = "menu";
    /// <summary>权限码（按钮）</summary>
    public string? permission { get; set; }
    public bool visible { get; set; } = true;
    public int status { get; set; } = 1;
    public int sort { get; set; }
    public List<MenuNodeDto> children { get; set; } = new();
}

/// <summary>菜单/按钮保存请求</summary>
public class MenuSaveRequest
{
    public string? id { get; set; }
    public string? parentId { get; set; }
    public string name { get; set; } = string.Empty;
    public string? path { get; set; }
    public string? component { get; set; }
    public string? icon { get; set; }
    public string type { get; set; } = "menu";
    public string? permission { get; set; }
    public bool visible { get; set; } = true;
    public int status { get; set; } = 1;
    public int sort { get; set; }
}

/// <summary>当前登录用户信息</summary>
public class CurrentUserDto
{
    public string userId { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public string? nickname { get; set; }
    public string? avatar { get; set; }
    public List<string> roles { get; set; } = new();
    public List<string> permissions { get; set; } = new();
    public List<MenuNodeDto> menus { get; set; } = new();
    /// <summary>所属租户 ID（0 = 平台层）</summary>
    public string tenantId { get; set; } = "0";
    /// <summary>所属租户名称</summary>
    public string tenantName { get; set; } = "平台";
    /// <summary>按密码策略判定是否需要强制改密（登录与取当前用户共用同一口径）</summary>
    public bool mustChangePassword { get; set; }
}

/// <summary>用户列表项</summary>
public class UserItemDto
{
    public string id { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public string? nickname { get; set; }
    public string? email { get; set; }
    public string? phone { get; set; }
    /// <summary>1 启用 / 0 禁用</summary>
    public int status { get; set; }
    public List<string> roleIds { get; set; } = new();
    public List<string> roleNames { get; set; } = new();
    public DateTime? createdAt { get; set; }
}

/// <summary>用户新增/编辑请求</summary>
public class UserSaveRequest
{
    public string? id { get; set; }
    public string username { get; set; } = string.Empty;
    public string? nickname { get; set; }
    public string? email { get; set; }
    public string? phone { get; set; }
    public string? password { get; set; }
    public int status { get; set; } = 1;
    public List<string>? roleIds { get; set; }
}

/// <summary>个人资料项（个人中心展示与编辑，仅含本人可自助的字段）</summary>
public class ProfileDto
{
    public string userId { get; set; } = string.Empty;
    public string username { get; set; } = string.Empty;
    public string? nickname { get; set; }
    public string? avatar { get; set; }
    public string? phone { get; set; }
    public string? email { get; set; }
    public DateTime? lastLoginTime { get; set; }
}

/// <summary>个人资料保存请求（昵称/头像/手机/邮箱）</summary>
public class ProfileSaveRequest
{
    public string? nickname { get; set; }
    public string? avatar { get; set; }
    public string? phone { get; set; }
    public string? email { get; set; }
}

/// <summary>修改自己密码请求（旧密码必校验，不可绕过）</summary>
public class ChangePasswordRequest
{
    public string? oldPassword { get; set; }
    public string? newPassword { get; set; }
}

/// <summary>角色项</summary>
public class RoleItemDto
{
    public string id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string code { get; set; } = string.Empty;
    public string? description { get; set; }
    public int status { get; set; }
    public DateTime? createdAt { get; set; }
}

/// <summary>角色保存请求</summary>
public class RoleSaveRequest
{
    public string? id { get; set; }
    public string name { get; set; } = string.Empty;
    public string code { get; set; } = string.Empty;
    public string? description { get; set; }
    public int status { get; set; } = 1;
    public List<string>? menuIds { get; set; }
    public List<string>? apiIds { get; set; }
}

/// <summary>角色权限分配请求</summary>
public class RolePermissionRequest
{
    public List<string> menuIds { get; set; } = new();
    public List<string> apiIds { get; set; } = new();
}

/// <summary>接口资源项</summary>
public class ApiItemDto
{
    public string id { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public string method { get; set; } = "GET";
    public string path { get; set; } = string.Empty;
    public string? service { get; set; }
    public string? permission { get; set; }
    public string? description { get; set; }
    public int status { get; set; }
    public DateTime? createdAt { get; set; }
}

/// <summary>接口资源保存请求</summary>
public class ApiSaveRequest
{
    public string? id { get; set; }
    public string name { get; set; } = string.Empty;
    public string method { get; set; } = "GET";
    public string path { get; set; } = string.Empty;
    public string? service { get; set; }
    public string? permission { get; set; }
    public string? description { get; set; }
    public int status { get; set; } = 1;
}

/// <summary>服务实例（监控中心）</summary>
public class ServiceInstanceDto
{
    public string instanceId { get; set; } = string.Empty;
    public string host { get; set; } = string.Empty;
    public int port { get; set; }
    public string status { get; set; } = "UP";
    public string? lastHeartbeat { get; set; }
}

/// <summary>服务项（监控中心）</summary>
public class ServiceItemDto
{
    public string serviceName { get; set; } = string.Empty;
    public string? displayName { get; set; }
    public int instances { get; set; }
    /// <summary>UP / DOWN / PARTIAL</summary>
    public string status { get; set; } = "UP";
    public string? lastHealthCheck { get; set; }
    public string? version { get; set; }
    public string? description { get; set; }
    public List<ServiceInstanceDto> instanceList { get; set; } = new();
}

/// <summary>日志项（日志中心）</summary>
public class LogItemDto
{
    public string id { get; set; } = string.Empty;
    public string timestamp { get; set; } = string.Empty;
    public string level { get; set; } = "INFO";
    public string service { get; set; } = string.Empty;
    public string? instance { get; set; }
    public string? traceId { get; set; }
    public string? thread { get; set; }
    public string? logger { get; set; }
    public string message { get; set; } = string.Empty;
    public string? exception { get; set; }
}

/// <summary>公告项（管理端列表与登录用户最新公告共用）</summary>
public class AnnouncementItemDto
{
    public string id { get; set; } = string.Empty;
    public string title { get; set; } = string.Empty;
    public string? content { get; set; }
    /// <summary>0 已下线，1 已发布</summary>
    public int status { get; set; }
    public string? publisherName { get; set; }
    public DateTime? publishTime { get; set; }
    public DateTime createdAt { get; set; }
}

/// <summary>用户导入结果（逐行校验错误明细）</summary>
public class UserImportResultDto
{
    /// <summary>文件中数据行总数（不含表头与示例行）</summary>
    public int total { get; set; }
    /// <summary>导入成功数</summary>
    public int success { get; set; }
    /// <summary>失败明细：行号 + 原因</summary>
    public List<UserImportErrorDto> errors { get; set; } = new();
}

/// <summary>用户导入单行错误</summary>
public class UserImportErrorDto
{
    /// <summary>数据行号（从 1 开始，不含表头）</summary>
    public int row { get; set; }
    public string reason { get; set; } = string.Empty;
}
