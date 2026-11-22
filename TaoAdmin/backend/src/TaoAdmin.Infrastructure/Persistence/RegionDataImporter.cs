using System.Text.Json;
using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;

namespace TaoAdmin.Infrastructure.Persistence;

/// <summary>
/// 行政区划数据导入（老板 2026-11-22 令⑪地址库）：
/// 内置数据包 regions.json（省/市/县/街道四级嵌套，来源民政部国家地名信息库，
/// 数据截止 2025-12-31，包内 version/source 字段记录口径）随仓库交付，
/// 初始化时 sys_region 为空则整包导入，幂等（重复执行不重复插入）。
/// 在线更新失败时可通过 <see cref="BuiltinRegionPackageProvider"/> 用本包兜底恢复。
/// </summary>
public static class RegionDataImporter
{
    /// <summary>数据包资源名（EmbeddedResource，随程序集走，单文件发布不丢文件）</summary>
    private const string ResourceName = "TaoAdmin.Infrastructure.Data.regions.json";

    /// <summary>数据包条目结构（code/name/level/children 嵌套）</summary>
    private sealed class PackageNode
    {
        public string code { get; set; } = "";
        public string name { get; set; } = "";
        public int level { get; set; }
        public List<PackageNode>? children { get; set; }
    }

    private sealed class Package
    {
        public string version { get; set; } = "";
        public string source { get; set; } = "";
        public List<PackageNode> nodes { get; set; } = [];
    }

    /// <summary>从内嵌数据包读取快照（供初始化导入与内置兜底复用）</summary>
    public static RegionSnapshot LoadBuiltinSnapshot()
    {
        var assembly = typeof(RegionDataImporter).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"区划数据包资源缺失：{ResourceName}");
        var pkg = JsonSerializer.Deserialize<Package>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("区划数据包解析结果为空");

        return new RegionSnapshot
        {
            Version = pkg.version,
            Source = pkg.source,
            Nodes = pkg.nodes.Select(ToDto).ToList()
        };
    }

    private static RegionNodeDto ToDto(PackageNode n) => new()
    {
        Code = n.code,
        Name = n.name,
        Level = n.level,
        Children = n.children?.Select(ToDto).ToList() ?? []
    };

    /// <summary>导入区划数据（幂等：sys_region 已有数据则跳过）。返回导入总条数（0 表示已有数据未导入）。</summary>
    public static int ImportIfEmpty(SnowflakeIdGenerator snowflake, ISqlSugarClient db)
    {
        if (db.Queryable<TaoAdmin.Domain.Entities.SysRegion>().Any())
            return 0;

        var snapshot = LoadBuiltinSnapshot();
        return new RegionWriteService(db).ReplaceAll(snapshot, snowflake);
    }
}

/// <summary>内置数据包提供者：更新失败时的兜底数据源（读取程序集内嵌 regions.json）</summary>
public class BuiltinRegionPackageProvider : IRegionPackageProvider
{
    public RegionSnapshot GetBuiltinSnapshot() => RegionDataImporter.LoadBuiltinSnapshot();
}
