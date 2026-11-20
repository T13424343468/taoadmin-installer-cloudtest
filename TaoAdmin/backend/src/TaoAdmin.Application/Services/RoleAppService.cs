using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Http;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>角色应用服务：CRUD、权限分配、已分配权限查询。</summary>
public class RoleAppService
{
    private readonly ISqlSugarClient _db;
    private readonly ISysRoleRepository _roles;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public RoleAppService(ISqlSugarClient db, ISysRoleRepository roles, Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _roles = roles; _snowflake = snowflake;
    }

    /// <summary>角色列表（全量）</summary>
    public async Task<List<RoleItemDto>> ListAsync()
    {
        var roles = await _db.Queryable<SysRole>().Where(r => r.IsDeleted == false).OrderBy(r => r.CreatedAt).ToListAsync();
        return roles.Select(ToItem).ToList();
    }

    /// <summary>新增/编辑角色（可同时带权限分配）</summary>
    public async Task SaveAsync(RoleSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.name) || string.IsNullOrWhiteSpace(req.code))
            throw new DomainException("角色名与角色编码不能为空");

        if (string.IsNullOrEmpty(req.id))
        {
            if (await _roles.ExistsCodeAsync(req.code))
                throw new DomainException("角色编码已存在");
            var role = new SysRole
            {
                Id = _snowflake.NextId(),
                RoleName = req.name,
                RoleCode = req.code,
                Status = req.status,
                Remark = req.description
            };
            await _roles.AddAsync(role);
            await _roles.ReplacePermissionsAsync(role.Id, Parse(req.menuIds), Parse(req.apiIds));
        }
        else
        {
            var role = await _roles.FindByIdAsync(long.Parse(req.id)) ?? throw new DomainException("角色不存在", 404);
            role.RoleName = req.name;
            role.RoleCode = req.code;
            role.Status = req.status;
            role.Remark = req.description;
            await _roles.UpdateAsync(role);
            await _roles.ReplacePermissionsAsync(role.Id, Parse(req.menuIds), Parse(req.apiIds));
        }
    }

    /// <summary>删除角色（软删 + 级联清理关联）</summary>
    public async Task DeleteAsync(long id) => await _roles.SoftDeleteCascadeAsync(id);

    /// <summary>角色已分配的权限（菜单ID + 接口ID）</summary>
    public async Task<(List<string> MenuIds, List<string> ApiIds)> GetPermissionsAsync(long roleId)
    {
        var menuIds = await _db.Queryable<SysRoleMenu>().Where(rm => rm.RoleId == roleId).Select(rm => rm.MenuId).ToListAsync();
        var apiIds = await _db.Queryable<SysRoleApi>().Where(ra => ra.RoleId == roleId).Select(ra => ra.ApiId).ToListAsync();
        return (menuIds.Select(x => x.ToString()).ToList(), apiIds.Select(x => x.ToString()).ToList());
    }

    /// <summary>分配权限（全量覆盖）</summary>
    public async Task AssignPermissionsAsync(long roleId, List<string> menuIds, List<string> apiIds)
        => await _roles.ReplacePermissionsAsync(roleId, Parse(menuIds), Parse(apiIds));

    private static RoleItemDto ToItem(SysRole r) => new()
    {
        id = r.Id.ToString(),
        name = r.RoleName,
        code = r.RoleCode,
        description = r.Remark,
        status = r.Status,
        createdAt = r.CreatedAt
    };

    private static List<long>? Parse(List<string>? ids) => ids?.Select(long.Parse).ToList();
}
