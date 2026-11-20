using TaoAdmin.Common.Entities;
using SqlSugar;

namespace TaoAdmin.Domain.Entities;

/// <summary>接口资源表（sys_api）：后端接口的权限元数据，供角色分配接口权限</summary>
[SugarTable("sys_api", TableDescription = "接口资源")]
public class SysApi : BaseEntity
{
    /// <summary>接口名称</summary>
    [SugarColumn(Length = 100, ColumnDescription = "接口名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>接口路径，如 /api/user/page</summary>
    [SugarColumn(Length = 200, ColumnDescription = "接口路径")]
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP 方法：GET / POST / PUT / DELETE</summary>
    [SugarColumn(Length = 10, ColumnDescription = "HTTP方法")]
    public string HttpMethod { get; set; } = "GET";

    /// <summary>权限码，如 sys:user:list</summary>
    [SugarColumn(Length = 100, ColumnDescription = "权限码")]
    public string PermsCode { get; set; } = string.Empty;

    /// <summary>状态：0 停用，1 启用</summary>
    [SugarColumn(ColumnDescription = "状态 0停用 1启用")]
    public int Status { get; set; } = 1;
}

/// <summary>角色-接口关联表（sys_role_api）：给角色分配接口权限</summary>
[SugarTable("sys_role_api", TableDescription = "角色接口关联")]
public class SysRoleApi : BaseEntity
{
    /// <summary>角色ID</summary>
    [SugarColumn(ColumnDescription = "角色ID")]
    public long RoleId { get; set; }

    /// <summary>接口ID</summary>
    [SugarColumn(ColumnDescription = "接口ID")]
    public long ApiId { get; set; }
}
