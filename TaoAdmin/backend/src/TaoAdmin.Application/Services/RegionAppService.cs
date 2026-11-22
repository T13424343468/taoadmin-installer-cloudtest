using TaoAdmin.Common.Http;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.Application.Services;

/// <summary>区划条目 DTO</summary>
public class RegionDto
{
    /// <summary>区划代码</summary>
    public string code { get; set; } = "";
    /// <summary>区划名称</summary>
    public string name { get; set; } = "";
    /// <summary>父级区划代码（省级为空）</summary>
    public string parentCode { get; set; } = "";
    /// <summary>层级：1 省，2 地级市，3 区县，4 街道（乡镇）</summary>
    public int level { get; set; }
    /// <summary>全路径（如：北京市 / 东城区 / 东华门街道）</summary>
    public string fullPath { get; set; } = "";
}

/// <summary>
/// 行政区划应用服务（老板 2026-11-22 21:02 令⑪地址库）：
/// 只读查询——按父级查子级、关键词搜索、按代码查详情；不做逐条编辑。
/// 数据来源民政部全国行政区划信息查询平台，随系统初始化导入。
/// </summary>
public class RegionAppService(ISqlSugarClient db)
{
    /// <summary>按父级代码查子级（parentCode 传空串查省级），按区划代码排序</summary>
    public async Task<List<RegionDto>> GetChildrenAsync(string? parentCode)
    {
        var parent = parentCode ?? string.Empty;
        var list = await db.Queryable<SysRegion>()
            .Where(r => r.ParentCode == parent)
            .OrderBy(r => r.Code)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    /// <summary>按关键词搜索名称（含全路径匹配），最多返回 50 条，按层级与代码排序</summary>
    public async Task<List<RegionDto>> SearchAsync(string? keyword)
    {
        var kw = (keyword ?? string.Empty).Trim();
        if (kw.Length == 0) return [];
        var list = await db.Queryable<SysRegion>()
            .Where(r => r.Name.Contains(kw) || r.FullPath.Contains(kw))
            .OrderBy(r => r.Level).OrderBy(r => r.Code)
            .Take(50)
            .ToListAsync();
        return list.Select(ToDto).ToList();
    }

    /// <summary>按区划代码查详情；不存在返回 null</summary>
    public async Task<RegionDto?> GetByCodeAsync(string code)
    {
        var region = await db.Queryable<SysRegion>().FirstAsync(r => r.Code == code);
        return region == null ? null : ToDto(region);
    }

    /// <summary>读取数据版本元数据（单行；未初始化时返回 null）</summary>
    public async Task<SysRegionMeta?> GetMetaAsync()
        => await db.Queryable<SysRegionMeta>().FirstAsync(m => m.Id == SysRegionMeta.SingleRowId);

    private static RegionDto ToDto(SysRegion r) => new()
    {
        code = r.Code, name = r.Name, parentCode = r.ParentCode,
        level = r.Level, fullPath = r.FullPath
    };
}
