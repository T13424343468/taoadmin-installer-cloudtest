using TaoAdmin.Common.Http;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>字典保存请求</summary>
public class DictTypeSaveRequest
{
    /// <summary>字典名称</summary>
    public string dictName { get; set; } = "";
    /// <summary>字典编码（唯一，创建后不可改）</summary>
    public string dictCode { get; set; } = "";
    /// <summary>状态 0停用 1启用</summary>
    public int status { get; set; } = 1;
    /// <summary>备注</summary>
    public string? remark { get; set; }
}

/// <summary>字典数据保存请求</summary>
public class DictDataSaveRequest
{
    /// <summary>所属字典编码</summary>
    public string dictCode { get; set; } = "";
    /// <summary>显示文本</summary>
    public string label { get; set; } = "";
    /// <summary>存储值</summary>
    public string value { get; set; } = "";
    /// <summary>排序号</summary>
    public int sort { get; set; }
    /// <summary>状态 0停用 1启用</summary>
    public int status { get; set; } = 1;
    /// <summary>备注</summary>
    public string? remark { get; set; }
}

/// <summary>
/// 数据字典应用服务：字典类型与字典数据的维护，供前端下拉框/状态转义使用。
/// 字典按租户隔离（平台公共字典 TenantId=0 全租户可见，与整体多租户规则一致）。
/// </summary>
public class DictAppService(ISqlSugarClient db, SnowflakeIdGenerator snowflake)
{
    /// <summary>字典类型分页</summary>
    public async Task<PageResult<DictTypeDto>> PageTypesAsync(int pageIndex, int pageSize, string? keyword)
    {
        RefAsync<int> total = 0;
        var list = await db.Queryable<SysDictType>()
            .Where(t => t.IsDeleted == false)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword),
                t => t.DictName.Contains(keyword!) || t.DictCode.Contains(keyword!))
            .OrderBy(t => t.CreatedAt, OrderByType.Desc)
            .ToPageListAsync(pageIndex, pageSize, total);
        return new PageResult<DictTypeDto>
        {
            Total = total,
            PageIndex = pageIndex,
            PageSize = pageSize,
            Items = list.Select(ToDto).ToList()
        };
    }

    /// <summary>按编码取字典的全部启用明细（前端下拉框数据源）</summary>
    public async Task<List<DictDataDto>> GetItemsAsync(string dictCode)
        => (await db.Queryable<SysDictData>()
            .Where(d => d.DictCode == dictCode && d.Status == 1 && d.IsDeleted == false)
            .OrderBy(d => d.Sort)
            .ToListAsync())
            .Select(d => new DictDataDto
            {
                id = d.Id.ToString(), label = d.Label, value = d.Value,
                sort = d.Sort, status = d.Status.ToString(), remark = d.Remark
            })
            .ToList();

    /// <summary>新增/编辑字典类型（幂等：带 id 为编辑）</summary>
    public async Task SaveTypeAsync(DictTypeSaveRequest req, string? id)
    {
        if (string.IsNullOrWhiteSpace(req.dictCode) || string.IsNullOrWhiteSpace(req.dictName))
            throw new DomainException("字典名称与编码不能为空", 400);
        if (string.IsNullOrEmpty(id))
        {
            var dup = await db.Queryable<SysDictType>().AnyAsync(t => t.DictCode == req.dictCode && t.IsDeleted == false);
            if (dup) throw new DomainException("字典编码已存在", 400);
            await db.Insertable(new SysDictType
            {
                Id = snowflake.NextId(), DictName = req.dictName, DictCode = req.dictCode,
                Status = req.status, Remark = req.remark
            }).ExecuteCommandAsync();
        }
        else
        {
            var entity = await db.Queryable<SysDictType>().FirstAsync(t => t.Id == long.Parse(id) && t.IsDeleted == false)
              ?? throw new DomainException("字典类型不存在", 404);
            entity.DictName = req.dictName; entity.Status = req.status; entity.Remark = req.remark;
            entity.UpdatedAt = DateTime.Now;
            await db.Updateable(entity).ExecuteCommandAsync();
        }
    }

    /// <summary>新增/编辑字典数据</summary>
    public async Task SaveDataAsync(DictDataSaveRequest req, string? id)
    {
        if (string.IsNullOrWhiteSpace(req.dictCode) || string.IsNullOrWhiteSpace(req.label) || string.IsNullOrWhiteSpace(req.value))
            throw new DomainException("字典编码、文本与值不能为空", 400);
        if (string.IsNullOrEmpty(id))
        {
            await db.Insertable(new SysDictData
            {
                Id = snowflake.NextId(), DictCode = req.dictCode, Label = req.label, Value = req.value,
                Sort = req.sort, Status = req.status, Remark = req.remark
            }).ExecuteCommandAsync();
        }
        else
        {
            var entity = await db.Queryable<SysDictData>().FirstAsync(d => d.Id == long.Parse(id) && d.IsDeleted == false)
              ?? throw new DomainException("字典数据不存在", 404);
            entity.Label = req.label; entity.Value = req.value; entity.Sort = req.sort;
            entity.Status = req.status; entity.Remark = req.remark; entity.UpdatedAt = DateTime.Now;
            await db.Updateable(entity).ExecuteCommandAsync();
        }
    }

    /// <summary>删除字典类型（软删，连同明细）</summary>
    public async Task DeleteTypeAsync(string id)
    {
        var tid = long.Parse(id);
        await db.Updateable<SysDictType>().SetColumns(t => new SysDictType { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(t => t.Id == tid).ExecuteCommandAsync();
        await db.Updateable<SysDictData>().SetColumns(d => new SysDictData { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(d => d.DictCode == (db.Queryable<SysDictType>().First(x => x.Id == tid).DictCode)).ExecuteCommandAsync();
    }

    /// <summary>删除字典数据（软删）</summary>
    public async Task DeleteDataAsync(string id)
        => await db.Updateable<SysDictData>().SetColumns(d => new SysDictData { IsDeleted = true, UpdatedAt = DateTime.Now })
            .Where(d => d.Id == long.Parse(id)).ExecuteCommandAsync();

    private static DictTypeDto ToDto(SysDictType t) => new()
    {
        id = t.Id.ToString(), dictName = t.DictName, dictCode = t.DictCode,
        status = t.Status.ToString(), remark = t.Remark, createdAt = t.CreatedAt
    };
}

// ============ DTO ============
public class DictTypeDto
{
    public string id { get; set; } = "";
    public string dictName { get; set; } = "";
    public string dictCode { get; set; } = "";
    public string status { get; set; } = "";
    public string? remark { get; set; }
    public DateTime createdAt { get; set; }
}

public class DictDataDto
{
    public string id { get; set; } = "";
    public string label { get; set; } = "";
    public string value { get; set; } = "";
    public int sort { get; set; }
    public string status { get; set; } = "";
    public string? remark { get; set; }
}
