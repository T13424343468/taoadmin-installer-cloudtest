using TaoAdmin.Application.Dtos;
using TaoAdmin.Common.Http;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Domain.Repositories;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>接口资源应用服务：权限元数据的 CRUD。</summary>
public class ApiAppService
{
    private readonly ISqlSugarClient _db;
    private readonly ISysApiRepository _apis;
    private readonly Common.Snowflake.SnowflakeIdGenerator _snowflake;

    public ApiAppService(ISqlSugarClient db, ISysApiRepository apis, Common.Snowflake.SnowflakeIdGenerator snowflake)
    {
        _db = db; _apis = apis; _snowflake = snowflake;
    }

    /// <summary>分页查询接口资源</summary>
    public async Task<PageResult<ApiItemDto>> PageAsync(int pageIndex, int pageSize, string? keyword)
    {
        var query = _db.Queryable<SysApi>().Where(a => a.IsDeleted == false)
            .WhereIF(!string.IsNullOrEmpty(keyword), a => a.Name.Contains(keyword!) || a.Path.Contains(keyword!));
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(a => a.CreatedAt)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PageResult<ApiItemDto>(total, pageIndex, pageSize, rows.Select(ToItem).ToList());
    }

    /// <summary>全部启用接口（供角色分配选择）</summary>
    public async Task<List<ApiItemDto>> ListActiveAsync()
    {
        var rows = await _db.Queryable<SysApi>().Where(a => a.IsDeleted == false && a.Status == 1).OrderBy(a => a.Path).ToListAsync();
        return rows.Select(ToItem).ToList();
    }

    /// <summary>新增/编辑接口资源</summary>
    public async Task SaveAsync(ApiSaveRequest req)
    {
        var method = (req.method ?? "GET").ToUpper();
        if (string.IsNullOrEmpty(req.id))
        {
            if (await _apis.ExistsAsync(req.path, method))
                throw new DomainException("同路径同方法的接口已存在");
            await _apis.AddAsync(new SysApi
            {
                Id = _snowflake.NextId(),
                Name = req.name,
                Path = req.path,
                HttpMethod = method,
                PermsCode = req.permission ?? string.Empty,
                Status = req.status
            });
        }
        else
        {
            await _apis.UpdateAsync(new SysApi
            {
                Id = long.Parse(req.id),
                Name = req.name,
                Path = req.path,
                HttpMethod = method,
                PermsCode = req.permission ?? string.Empty,
                Status = req.status
            });
        }
    }

    /// <summary>删除接口资源（软删 + 清理角色关联）</summary>
    public async Task DeleteAsync(long id) => await _apis.SoftDeleteAsync(id);

    private static ApiItemDto ToItem(SysApi a) => new()
    {
        id = a.Id.ToString(),
        name = a.Name,
        method = a.HttpMethod,
        path = a.Path,
        permission = a.PermsCode,
        status = a.Status,
        createdAt = a.CreatedAt
    };
}
