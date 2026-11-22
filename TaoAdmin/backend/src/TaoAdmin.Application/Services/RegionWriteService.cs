using SqlSugar;
using TaoAdmin.Domain.Entities;

namespace TaoAdmin.Application.Services;

/// <summary>
/// 行政区划整包写入（老板 2026-11-22 令⑪地址库）：
/// 事务内清空 sys_region 并分批写入新数据包，同时更新 sys_region_meta 版本行；
/// 任一批失败整体回滚，本地原数据保持不动。供初始化导入与在线更新复用。
/// </summary>
public class RegionWriteService(ISqlSugarClient db)
{
    /// <summary>分批写入批大小（4 万余条分批插入，避免单语句超长）</summary>
    public const int BatchSize = 1000;

    /// <summary>写入进度回调（已写入条数, 总条数）</summary>
    public delegate void WriteProgressHandler(int written, int total);

    /// <summary>
    /// 整包替换：事务内删除全部旧数据、分批写入快照、更新版本元数据行。
    /// 返回写入总条数。onProgress 可空，用于写入阶段百分比上报。
    /// </summary>
    public int ReplaceAll(RegionSnapshot snapshot, TaoAdmin.Common.Snowflake.SnowflakeIdGenerator snowflake, WriteProgressHandler? onProgress = null)
        => ReplaceAllCore(snapshot, snowflake, onProgress, async: false).GetAwaiter().GetResult();

    /// <summary>整包替换（异步版），语义同 <see cref="ReplaceAll"/>。</summary>
    public Task<int> ReplaceAllAsync(RegionSnapshot snapshot, TaoAdmin.Common.Snowflake.SnowflakeIdGenerator snowflake, WriteProgressHandler? onProgress = null)
        => ReplaceAllCore(snapshot, snowflake, onProgress, async: true);

    private async Task<int> ReplaceAllCore(RegionSnapshot snapshot, TaoAdmin.Common.Snowflake.SnowflakeIdGenerator snowflake, WriteProgressHandler? onProgress, bool async)
    {
        var rows = snapshot.Flatten();
        var nameByCode = rows.ToDictionary(r => r.Code, r => r.Name);
        var now = DateTime.Now;
        var entities = rows.Select(r =>
        {
            // 全路径：逐级拼接父级名称，省级即自身名称
            var parentPath = string.IsNullOrEmpty(r.ParentCode) ? "" : (nameByCode.GetValueOrDefault(r.ParentCode, "") + " / ");
            return new SysRegion
            {
                Id = snowflake.NextId(),
                Code = r.Code,
                Name = r.Name,
                ParentCode = r.ParentCode ?? string.Empty,
                Level = r.Level,
                FullPath = parentPath + r.Name,
                CreatedAt = now,
                UpdatedAt = now
            };
        }).ToList();

        var counts = RegionSnapshot.CountByLevel(entities.Select(e => (e.Code, e.Name, (string?)e.ParentCode, e.Level)));
        var meta = new SysRegionMeta
        {
            Id = SysRegionMeta.SingleRowId,
            Version = snapshot.Version,
            Source = snapshot.Source,
            FetchedAt = now,
            ProvinceCount = counts.Level1,
            CityCount = counts.Level2,
            CountyCount = counts.Level3,
            TownCount = counts.Level4,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            db.Ado.BeginTran();
            if (async) await db.Deleteable<SysRegion>().ExecuteCommandAsync();
            else db.Deleteable<SysRegion>().ExecuteCommand();

            var written = 0;
            foreach (var batch in entities.Chunk(BatchSize))
            {
                if (async) await db.Insertable(batch.ToList()).ExecuteCommandAsync();
                else db.Insertable(batch.ToList()).ExecuteCommand();
                written += batch.Length;
                onProgress?.Invoke(written, entities.Count);
            }

            // 版本元数据：单行 upsert（首次插入，其后更新）
            var exists = async
                ? await db.Queryable<SysRegionMeta>().AnyAsync(m => m.Id == SysRegionMeta.SingleRowId)
                : db.Queryable<SysRegionMeta>().Any(m => m.Id == SysRegionMeta.SingleRowId);
            if (exists)
            {
                if (async) await db.Updateable(meta).WhereColumns(m => m.Id).ExecuteCommandAsync();
                else db.Updateable(meta).WhereColumns(m => m.Id).ExecuteCommand();
            }
            else
            {
                if (async) await db.Insertable(meta).ExecuteCommandAsync();
                else db.Insertable(meta).ExecuteCommand();
            }

            db.Ado.CommitTran();
            return entities.Count;
        }
        catch
        {
            try { db.Ado.RollbackTran(); } catch { /* 回滚失败不再抛出，保留原始异常 */ }
            throw;
        }
    }
}
