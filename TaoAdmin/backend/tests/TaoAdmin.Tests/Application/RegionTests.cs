using SqlSugar;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using TaoAdmin.Infrastructure.Persistence;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 行政区划（⑪地址库，SQLite 真库测试）：
/// 断言先行清单——①四级导入数量与数据包一致且幂等；②按 parentCode 查子级正确层级且按 code 排序；
/// ③关键词搜索命中名称并带全路径；④按 code 查详情；⑤在线更新成功整包替换并记录前后条数与耗时；
/// ⑥校验失败本地数据原样不动且报错写明卡点；⑦连接失败同样兜底；⑧同一时间只允许一个更新任务；
/// ⑨内置数据包恢复兜底可用。
/// </summary>
public class RegionTests
{
    private static (SqlSugarClient Db, RegionAppService Svc, SnowflakeIdGenerator Snow, RegionWriteService Writer) Create()
    {
        var db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "DataSource=:memory:",
            DbType = SqlSugar.DbType.Sqlite,
            IsAutoCloseConnection = false
        });
        db.CodeFirst.InitTables<SysRegion>();
        db.CodeFirst.InitTables<SysRegionMeta>();
        var snow = new SnowflakeIdGenerator(1, 1);
        return (db, new RegionAppService(db), snow, new RegionWriteService(db));
    }

    /// <summary>小样本区划（2 省 2 市 3 县 4 街道）手工建库，供查询断言</summary>
    private static void SeedSample(SqlSugarClient db, SnowflakeIdGenerator snow)
    {
        var now = DateTime.Now;
        SysRegion R(string code, string name, string parent, int level, string path) =>
            new() { Id = snow.NextId(), Code = code, Name = name, ParentCode = parent, Level = level, FullPath = path, CreatedAt = now, UpdatedAt = now };

        db.Insertable(new List<SysRegion>
        {
            R("11", "北京市", "", 1, "北京市"),
            R("32", "江苏省", "", 1, "江苏省"),
            R("110101", "东城区", "11", 3, "北京市 / 东城区"),
            R("320500", "苏州市", "32", 2, "江苏省 / 苏州市"),
            R("320600", "南通市", "32", 2, "江苏省 / 南通市"),
            R("110101001", "东华门街道", "110101", 4, "北京市 / 东城区 / 东华门街道"),
            R("320500001", "姑苏街道", "320500", 4, "江苏省 / 苏州市 / 姑苏街道"),
            R("320500002", "虎丘街道", "320500", 4, "江苏省 / 苏州市 / 虎丘街道"),
            R("320600001", "通州街道", "320600", 4, "江苏省 / 南通市 / 通州街道"),
        }).ExecuteCommand();
    }

    // ==================== 数据包导入（初始化） ====================

    [Fact]
    public async Task ImportIfEmpty_FourLevels_MatchesPackage_AndIdempotent()
    {
        var (db, svc, snow, _) = Create();

        var first = RegionDataImporter.ImportIfEmpty(snow, db);
        var total = db.Queryable<SysRegion>().Count();

        // 幂等：第二次导入不重复插入
        var second = RegionDataImporter.ImportIfEmpty(snow, db);
        Assert.Equal(total, db.Queryable<SysRegion>().Count());
        Assert.Equal(0, second);

        // 数量与数据包一致（民政部国家地名信息库口径：33 省 / 333 地级 / 2845 县级 / 38723 乡级）
        var pkg = RegionDataImporter.LoadBuiltinSnapshot();
        var rows = pkg.Flatten();
        var counts = RegionSnapshot.CountByLevel(rows);
        Assert.Equal(rows.Count, first);
        Assert.Equal(counts.Level1, db.Queryable<SysRegion>().Count(r => r.Level == 1));
        Assert.Equal(counts.Level2, db.Queryable<SysRegion>().Count(r => r.Level == 2));
        Assert.Equal(counts.Level3, db.Queryable<SysRegion>().Count(r => r.Level == 3));
        Assert.Equal(counts.Level4, db.Queryable<SysRegion>().Count(r => r.Level == 4));
        Assert.True(counts.Level4 > 30000, $"街道（乡镇）应有 3 万条以上，实际 {counts.Level4}");

        // 版本元数据随导入写入
        var meta = await svc.GetMetaAsync();
        Assert.NotNull(meta);
        Assert.Equal(pkg.Version, meta!.Version);
        Assert.Equal(rows.Count, meta.TotalCount);

        // 导入后查询可用：北京 → 东城区 → 东华门街道（直辖市跳过地级层级，省级码为 6 位）
        var children = await svc.GetChildrenAsync("110000");
        Assert.Contains(children, c => c.code == "110101" && c.name == "东城区");
        var streets = await svc.GetChildrenAsync("110101");
        Assert.Contains(streets, s => s.code == "110101001" && s.name == "东华门街道" && s.level == 4);
    }

    // ==================== 只读查询 ====================

    [Fact]
    public async Task GetChildrenAsync_ByParent_ReturnsSortedChildren()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);

        var cities = await svc.GetChildrenAsync("32");
        Assert.Equal(2, cities.Count);
        Assert.Equal(new[] { "320500", "320600" }, cities.Select(c => c.code).ToArray());

        var provinces = await svc.GetChildrenAsync("");
        Assert.Equal(2, provinces.Count);
        Assert.All(provinces, p => Assert.Equal(1, p.level));
    }

    [Fact]
    public async Task SearchAsync_ByName_ReturnsWithFullPath()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);

        var hits = await svc.SearchAsync("苏州");
        // 名称或全路径含「苏州」均命中：苏州市 + 姑苏街道 + 虎丘街道（路径含「苏州市」）
        Assert.Equal(3, hits.Count);
        var hit = Assert.Single(hits, h => h.code == "320500");
        Assert.Equal("江苏省 / 苏州市", hit.fullPath);

        // 全路径关键词也命中：搜「南通市 / 通州街道」
        var pathHits = await svc.SearchAsync("南通市 / 通州街道");
        Assert.Contains(pathHits, h => h.code == "320600001");
    }

    [Fact]
    public async Task GetByCodeAsync_ReturnsDetailOrNot()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);

        var detail = await svc.GetByCodeAsync("110101001");
        Assert.NotNull(detail);
        Assert.Equal("东华门街道", detail!.name);
        Assert.Null(await svc.GetByCodeAsync("999999"));
    }

    // ==================== 在线更新（后台任务） ====================

    /// <summary>可控假数据源：支持注入快照/异常与放行闸，用于成功/失败/并发三路断言</summary>
    private sealed class FakeSource : IRegionDataSource
    {
        public RegionSnapshot? Snapshot;
        public Exception? Throw;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RegionSnapshot> FetchLatestAsync(Action<RegionFetchProgress> onProgress, CancellationToken ct)
        {
            onProgress(new RegionFetchProgress { Stage = "连接数据源", FetchedCount = 0 });
            Started.TrySetResult();
            if (Throw != null) throw Throw;
            await Release.Task;
            onProgress(new RegionFetchProgress { Stage = "抓取数据", FetchedCount = Snapshot!.Flatten().Count, Percent = 70 });
            return Snapshot!;
        }
    }

    /// <summary>
    /// 构造 25 省小快照（满足校验器省级数量与乡级规模下限）：
    /// 每省 1 市 1 区 401 街道，共 25×404 = 10100 条。
    /// </summary>
    private static RegionSnapshot SmallSnapshot(string version = "测试版本·数据截止2026年12月31日")
    {
        var nodes = new List<RegionNodeDto>();
        for (var p = 1; p <= 25; p++)
        {
            var p2 = $"{p:00}";
            var streets = Enumerable.Range(1, 401)
                .Select(t => new RegionNodeDto { Code = $"{p2}0101{t:000}", Name = $"测试街道{p}-{t}", Level = 4 })
                .ToList();
            nodes.Add(new RegionNodeDto
            {
                Code = $"{p2}0000", Name = $"测试省{p}", Level = 1,
                Children =
                [
                    new RegionNodeDto
                    {
                        Code = $"{p2}0100", Name = $"测试市{p}", Level = 2,
                        Children =
                        [
                            new RegionNodeDto { Code = $"{p2}0101", Name = $"测试区{p}", Level = 3, Children = streets }
                        ]
                    }
                ]
            });
        }
        return new RegionSnapshot { Version = version, Source = "测试数据源", Nodes = nodes };
    }

    private static RegionUpdateService NewUpdateService(SqlSugarClient db, IRegionDataSource source, SnowflakeIdGenerator snow)
        => new(db, source, new BuiltinRegionPackageProvider(), new RegionWriteService(db), snow,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RegionUpdateService>.Instance);

    /// <summary>等待假数据源进入抓取（最多 5 秒，超时即失败）</summary>
    private static async Task WaitStartedAsync(FakeSource source)
    {
        var done = await Task.WhenAny(source.Started.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(source.Started.Task, done);
    }

    /// <summary>轮询等待更新任务离开 Running 状态（最多 10 秒）</summary>
    private static async Task WaitDoneAsync(RegionUpdateService updater)
    {
        for (var i = 0; i < 200 && updater.GetProgress().Status == RegionUpdateStatus.Running; i++)
            await Task.Delay(50);
    }

    [Fact]
    public async Task StartOnlineUpdate_ReplacesAll_AndRecordsSummary()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);
        var source = new FakeSource { Snapshot = SmallSnapshot() };
        var updater = NewUpdateService(db, source, snow);

        // 点更新立即返回（任务后台执行）
        var (ok, _) = updater.StartOnlineUpdate();
        Assert.True(ok);
        Assert.Equal(RegionUpdateStatus.Running, updater.GetProgress().Status);

        await WaitStartedAsync(source);
        source.Release.TrySetResult();
        await WaitDoneAsync(updater);

        // 成功：整包替换 + 前后条数与耗时
        var progress = updater.GetProgress();
        Assert.True(progress.Status == RegionUpdateStatus.Success, $"更新未成功：{progress.Error}");
        Assert.Equal(10100, db.Queryable<SysRegion>().Count());
        Assert.Equal(10100, progress.AfterCount);
        Assert.Equal(9, progress.BeforeCount);
        Assert.NotNull(progress.DurationSeconds);
        Assert.Equal("测试版本·数据截止2026年12月31日", progress.Version);

        // 版本元数据同步更新
        var meta = await svc.GetMetaAsync();
        Assert.NotNull(meta);
        Assert.Equal("测试版本·数据截止2026年12月31日", meta!.Version);
        Assert.Equal(10100, meta.TotalCount);
    }

    [Fact]
    public async Task Update_InvalidSnapshot_FailsAtValidation_AndKeepsLocalData()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);
        var before = db.Queryable<SysRegion>().Count();
        var metaBefore = await svc.GetMetaAsync();

        // 快照含重复代码：校验必须拦下
        var bad = SmallSnapshot();
        bad.Nodes[0].Children[0].Children[0].Children.Add(
            new RegionNodeDto { Code = "010101001", Name = "重复街道", Level = 4 });
        var source = new FakeSource { Snapshot = bad };
        var updater = NewUpdateService(db, source, snow);

        updater.StartOnlineUpdate();
        await WaitStartedAsync(source);
        source.Release.TrySetResult();
        await WaitDoneAsync(updater);

        var progress = updater.GetProgress();
        Assert.Equal(RegionUpdateStatus.Failed, progress.Status);
        Assert.StartsWith("校验失败", progress.Error);
        Assert.Contains("重复", progress.Error);

        // 本地数据原样不动
        Assert.Equal(before, db.Queryable<SysRegion>().Count());
        var metaAfter = await svc.GetMetaAsync();
        Assert.Equal(metaBefore?.Version, metaAfter?.Version);
    }

    [Fact]
    public async Task Update_SourceThrows_FailsAtStage_AndKeepsLocalData()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);
        var before = db.Queryable<SysRegion>().Count();

        var source = new FakeSource { Throw = new InvalidOperationException("接口 queryXzqhChildren 返回 HTTP 502") };
        var updater = NewUpdateService(db, source, snow);

        updater.StartOnlineUpdate();
        await WaitStartedAsync(source);
        source.Release.TrySetResult(); // 保险放行（异常路径不会等闸）
        await WaitDoneAsync(updater);

        var progress = updater.GetProgress();
        Assert.Equal(RegionUpdateStatus.Failed, progress.Status);
        Assert.Contains("连接数据源", progress.Error);
        Assert.Contains("502", progress.Error);
        Assert.Equal(before, db.Queryable<SysRegion>().Count());
    }

    [Fact]
    public async Task StartOnlineUpdate_WhileRunning_IsRejected()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);
        var source = new FakeSource { Snapshot = SmallSnapshot() };
        var updater = NewUpdateService(db, source, snow);

        var (ok1, _) = updater.StartOnlineUpdate();
        Assert.True(ok1);

        // 任务执行中重复点击：拦截
        var (ok2, msg2) = updater.StartOnlineUpdate();
        Assert.False(ok2);
        Assert.Contains("已有更新任务", msg2);

        // 内置恢复同样被单任务闸拦截
        var (ok3, msg3) = updater.StartBuiltinRestore();
        Assert.False(ok3);
        Assert.Contains("已有更新任务", msg3);

        source.Release.TrySetResult();
        await WaitStartedAsync(source);
        await WaitDoneAsync(updater);
    }

    [Fact]
    public async Task BuiltinRestore_ReplacesWithPackage()
    {
        var (db, svc, snow, _) = Create();
        SeedSample(db, snow);
        var source = new FakeSource();
        var updater = NewUpdateService(db, source, snow);

        var (ok, _) = updater.StartBuiltinRestore();
        Assert.True(ok);
        for (var i = 0; i < 400 && updater.GetProgress().Status == RegionUpdateStatus.Running; i++)
            await Task.Delay(50);

        var progress = updater.GetProgress();
        Assert.Equal(RegionUpdateStatus.Success, progress.Status);
        Assert.Equal(9, progress.BeforeCount);

        // 恢复后数量与内置包一致，版本回到内置包口径
        var pkg = RegionDataImporter.LoadBuiltinSnapshot();
        Assert.Equal(pkg.Flatten().Count, db.Queryable<SysRegion>().Count());
        var meta = await svc.GetMetaAsync();
        Assert.Equal(pkg.Version, meta!.Version);
    }
}
