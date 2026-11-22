using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SqlSugar;
using TaoAdmin.Common.Snowflake;

namespace TaoAdmin.Application.Services;

/// <summary>区划数据节点（数据包/在线抓取共用，children 嵌套）</summary>
public class RegionNodeDto
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public List<RegionNodeDto> Children { get; set; } = [];
}

/// <summary>一份完整的区划数据快照（整包替换的最小单位）</summary>
public class RegionSnapshot
{
    /// <summary>数据版本（如：民政部国家地名信息库·数据截止2025年12月31日）</summary>
    public string Version { get; set; } = "";

    /// <summary>数据来源说明</summary>
    public string Source { get; set; } = "";

    /// <summary>省为根的嵌套节点（直辖市下直接挂区县，层级可跳级）</summary>
    public List<RegionNodeDto> Nodes { get; set; } = [];

    /// <summary>展平为带父码的行（ParentCode 由遍历栈推导）</summary>
    public List<(string Code, string Name, string? ParentCode, int Level)> Flatten()
    {
        var rows = new List<(string, string, string?, int)>();
        void Walk(RegionNodeDto node, string? parent)
        {
            rows.Add((node.Code, node.Name, parent, node.Level));
            foreach (var child in node.Children) Walk(child, node.Code);
        }
        foreach (var root in Nodes) Walk(root, null);
        return rows;
    }

    /// <summary>按层级统计条数</summary>
    public static (int Level1, int Level2, int Level3, int Level4) CountByLevel(
        IEnumerable<(string Code, string Name, string? ParentCode, int Level)> rows)
    {
        int l1 = 0, l2 = 0, l3 = 0, l4 = 0;
        foreach (var r in rows)
        {
            switch (r.Level)
            {
                case 1: l1++; break;
                case 2: l2++; break;
                case 3: l3++; break;
                case 4: l4++; break;
            }
        }
        return (l1, l2, l3, l4);
    }
}

/// <summary>抓取进度上报（阶段、已抓条数、可选的百分比提示）</summary>
public class RegionFetchProgress
{
    /// <summary>阶段名（连接数据源 / 抓取数据）</summary>
    public string Stage { get; set; } = "";

    /// <summary>已抓取条数</summary>
    public long FetchedCount { get; set; }

    /// <summary>抓取阶段百分比提示（0-100，可空表示未知）</summary>
    public int? Percent { get; set; }
}

/// <summary>
/// 区划数据源抽象：在线抓取实现见 Infrastructure（民政部国家地名信息库接口）；
/// 测试注入假实现。实现方负责解析与抓取进度上报，抛出异常即视为该阶段失败。
/// </summary>
public interface IRegionDataSource
{
    /// <summary>抓取最新区划数据快照</summary>
    Task<RegionSnapshot> FetchLatestAsync(Action<RegionFetchProgress> onProgress, CancellationToken ct);
}

/// <summary>内置数据包提供者（Infrastructure 内嵌资源实现，更新失败时的兜底数据）</summary>
public interface IRegionPackageProvider
{
    /// <summary>读取内置四级数据包快照</summary>
    RegionSnapshot GetBuiltinSnapshot();
}

/// <summary>更新任务状态</summary>
public enum RegionUpdateStatus
{
    /// <summary>空闲（从未更新或刚启动被覆盖）</summary>
    Idle = 0,
    /// <summary>执行中</summary>
    Running = 1,
    /// <summary>成功</summary>
    Success = 2,
    /// <summary>失败</summary>
    Failed = 3
}

/// <summary>更新任务进度快照（页面轮询展示用）</summary>
public class RegionUpdateProgress
{
    public RegionUpdateStatus Status { get; set; } = RegionUpdateStatus.Idle;
    /// <summary>当前阶段：连接数据源 / 抓取数据 / 校验 / 写入 / 完成</summary>
    public string Stage { get; set; } = "";
    /// <summary>整体百分比 0-100</summary>
    public int Percent { get; set; }
    /// <summary>已抓取条数</summary>
    public long FetchedCount { get; set; }
    /// <summary>失败原因（含卡在哪一步）</summary>
    public string? Error { get; set; }
    /// <summary>成功后：新数据版本</summary>
    public string? Version { get; set; }
    /// <summary>成功后：替换前总条数</summary>
    public int? BeforeCount { get; set; }
    /// <summary>成功后：替换后总条数</summary>
    public int? AfterCount { get; set; }
    /// <summary>成功后：耗时（秒，一位小数）</summary>
    public double? DurationSeconds { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    /// <summary>本次更新数据来源（online=在线抓取 builtin=内置数据包）</summary>
    public string SourceKind { get; set; } = "";
}

/// <summary>
/// 区划快照校验（老板令：校验通过才整包替换）。
/// 规则：非空、省级数量合理、代码唯一且为数字、层级与码长匹配、父子归属一致、名称非空。
/// 校验失败抛 <see cref="InvalidOperationException"/>，消息含具体规则与示例，供页面明确报错。
/// </summary>
public static class RegionSnapshotValidator
{
    public static void Validate(RegionSnapshot snapshot)
    {
        if (snapshot.Nodes.Count == 0)
            throw new InvalidOperationException("校验失败：数据包为空（未解析到任何省级节点）");

        var seen = new HashSet<string>();
        var rows = snapshot.Flatten();
        foreach (var (code, name, parentCode, level) in rows)
        {
            if (!code.All(char.IsDigit) || code.Length is not (6 or 9))
                throw new InvalidOperationException($"校验失败：区划代码应为 6 位或 9 位数字，遇到「{code}」（{name}）");
            if (!seen.Add(code))
                throw new InvalidOperationException($"校验失败：区划代码重复「{code}」（{name}）");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException($"校验失败：区划「{code}」名称为空");
            var expectLen = level == 4 ? 9 : 6;
            if (code.Length != expectLen)
                throw new InvalidOperationException($"校验失败：层级 {level} 的代码「{code}」长度应为 {expectLen} 位");
            if (level == 1 && !code.EndsWith("0000"))
                throw new InvalidOperationException($"校验失败：省级代码「{code}」应以 0000 结尾");
            if (level == 2 && (!code.EndsWith("00") || code.EndsWith("0000")))
                throw new InvalidOperationException($"校验失败：地级市代码「{code}」应以 00 结尾且非省级码");
            if (parentCode != null && !code.StartsWith(parentCode[..2]))
                throw new InvalidOperationException($"校验失败：区划「{code}」与父级「{parentCode}」归属不一致（省级识别码不匹配）");
        }

        var provinces = rows.Count(r => r.Level == 1);
        if (provinces is < 20 or > 40)
            throw new InvalidOperationException($"校验失败：省级节点数量 {provinces} 不在合理范围（20-40）");
        var towns = rows.Count(r => r.Level == 4);
        if (towns < 10000)
            throw new InvalidOperationException($"校验失败：街道（乡镇）条数 {towns} 过少，疑似数据不完整");
    }
}

/// <summary>
/// 行政区划更新编排（老板 2026-11-22 令⑪地址库 + 长期设计原则）：
/// 点更新立即返回，任务后台执行（关页面照跑）；进度实时可查（连接数据源/已抓条数/校验/写入百分比/完成失败）；
/// 同一时间只允许一个更新任务，重复点拦截；校验通过才整包替换，失败本地数据原样不动。
/// </summary>
public class RegionUpdateService(
    ISqlSugarClient db,
    IRegionDataSource dataSource,
    IRegionPackageProvider packageProvider,
    RegionWriteService writer,
    SnowflakeIdGenerator snowflake,
    ILogger<RegionUpdateService> logger)
{
    /// <summary>0=空闲 1=执行中（Interlocked 单任务闸）</summary>
    private int _running;

    private RegionUpdateProgress _state = new();
    private readonly object _stateLock = new();

    /// <summary>读取当前进度快照（页面轮询；刷新页面进度仍在）</summary>
    public RegionUpdateProgress GetProgress()
    {
        lock (_stateLock) { return Clone(_state); }
    }

    /// <summary>当前是否有任务在跑</summary>
    public bool IsRunning => Interlocked.CompareExchange(ref _running, 0, 0) == 1;

    /// <summary>启动在线更新（立即返回；已有任务在跑时返回 false 拦截）</summary>
    public (bool Ok, string Message) StartOnlineUpdate()
        => Start(online: true);

    /// <summary>从内置数据包恢复（更新失败时的兜底入口，同样后台执行并占用单任务闸）</summary>
    public (bool Ok, string Message) StartBuiltinRestore()
        => Start(online: false);

    private (bool, string) Start(bool online)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return (false, "已有更新任务在执行，请等待完成后再试");

        var before = db.Queryable<TaoAdmin.Domain.Entities.SysRegion>().Count();
        lock (_stateLock)
        {
            _state = new RegionUpdateProgress
            {
                Status = RegionUpdateStatus.Running,
                Stage = online ? "连接数据源" : "读取内置数据包",
                Percent = 0,
                SourceKind = online ? "online" : "builtin",
                BeforeCount = before,
                StartedAt = DateTime.Now
            };
        }
        logger.LogInformation("区划更新任务启动（{Kind}），当前条数 {Count}", online ? "在线抓取" : "内置数据包", before);

        // 后台执行：接口立即返回，关页面任务照跑（长期设计原则：大任务默认后台跑不卡屏）
        _ = Task.Run(() => RunAsync(online));
        return (true, online ? "更新任务已启动，可离开本页面，进度稍后可查" : "内置数据包恢复任务已启动");
    }

    private async Task RunAsync(bool online)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            RegionSnapshot snapshot;
            if (online)
            {
                snapshot = await dataSource.FetchLatestAsync(p => UpdateState(s =>
                {
                    s.Stage = p.Stage;
                    s.FetchedCount = p.FetchedCount;
                    // 抓取阶段占 0-70%（数据源未给百分比时按已抓条数估算，封顶 70）
                    s.Percent = p.Percent ?? Math.Min(70, (int)Math.Min(70, p.FetchedCount / 600));
                }), CancellationToken.None);
            }
            else
            {
                UpdateState(s => { s.Stage = "读取内置数据包"; s.Percent = 5; });
                snapshot = packageProvider.GetBuiltinSnapshot();
                UpdateState(s => { s.FetchedCount = snapshot.Flatten().Count; s.Percent = 70; });
            }

            // 校验阶段：不通过则抛异常，本地数据原样不动
            UpdateState(s => { s.Stage = "校验"; s.Percent = Math.Max(s.Percent, 72); });
            RegionSnapshotValidator.Validate(snapshot);

            // 写入阶段：事务整包替换 + 版本元数据
            UpdateState(s => { s.Stage = "写入"; s.Percent = 75; });
            var after = await writer.ReplaceAllAsync(snapshot, snowflake, (written, total) =>
                UpdateState(s => { s.Percent = 75 + (int)(25.0 * written / Math.Max(1, total)); }));

            sw.Stop();
            RegionUpdateProgress done;
            lock (_stateLock)
            {
                _state.Status = RegionUpdateStatus.Success;
                _state.Stage = "完成";
                _state.Percent = 100;
                _state.AfterCount = after;
                _state.Version = snapshot.Version;
                _state.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                _state.EndedAt = DateTime.Now;
                done = Clone(_state);
            }
            logger.LogInformation("区划更新完成：{Version}，{Before} → {After} 条，耗时 {Sec}s",
                snapshot.Version, _state.BeforeCount, after, _state.DurationSeconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            lock (_stateLock)
            {
                _state.Status = RegionUpdateStatus.Failed;
                _state.Stage = _state.Stage is "完成" ? "写入" : _state.Stage;
                // 校验器抛出的消息已含「校验失败：」前缀与具体规则，直接展示；其余统一补阶段前缀
                _state.Error = ex.Message.StartsWith("校验失败", StringComparison.Ordinal)
                    ? ex.Message
                    : $"{_state.Stage}失败：{ex.Message}";
                _state.DurationSeconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                _state.EndedAt = DateTime.Now;
            }
            logger.LogError(ex, "区划更新失败（卡在 {Stage}），本地数据未改动", _state.Stage);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void UpdateState(Action<RegionUpdateProgress> mutate)
    {
        lock (_stateLock) { mutate(_state); }
    }

    private static RegionUpdateProgress Clone(RegionUpdateProgress s) => new()
    {
        Status = s.Status,
        Stage = s.Stage,
        Percent = s.Percent,
        FetchedCount = s.FetchedCount,
        Error = s.Error,
        Version = s.Version,
        BeforeCount = s.BeforeCount,
        AfterCount = s.AfterCount,
        DurationSeconds = s.DurationSeconds,
        StartedAt = s.StartedAt,
        EndedAt = s.EndedAt,
        SourceKind = s.SourceKind
    };
}
