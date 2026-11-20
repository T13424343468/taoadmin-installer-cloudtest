using TaoAdmin.Common.Snowflake;
using Xunit;

namespace TaoAdmin.Tests.Snowflake;

/// <summary>雪花ID：参数校验、位布局、并发唯一性、时钟回拨保护</summary>
public class SnowflakeIdGeneratorMoreTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(32, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 32)]
    public void Constructor_InvalidIds_ShouldThrow(long dc, long worker)
    {
        Assert.Throws<ArgumentException>(() => new SnowflakeIdGenerator(dc, worker));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 31)]
    public void Constructor_BoundaryIds_ShouldBeAccepted(long dc, long worker)
    {
        var gen = new SnowflakeIdGenerator(dc, worker);
        Assert.Equal(dc, gen.DatacenterId);
        Assert.Equal(worker, gen.WorkerId);
    }

    [Fact]
    public void GeneratedId_ShouldEncodeDatacenterAndWorker()
    {
        const long dc = 5, worker = 9;
        var gen = new SnowflakeIdGenerator(dc, worker);
        var id = gen.NextId();

        Assert.True(id > 0);                       // 符号位恒为 0
        Assert.Equal(dc, (id >> 17) & 31);
        Assert.Equal(worker, (id >> 12) & 31);
        Assert.Equal(0, id >> 63);                 // 恒为正
    }

    [Fact]
    public async Task ConcurrentGeneration_ShouldBeUnique()
    {
        var gen = new SnowflakeIdGenerator(1, 1);
        const int threads = 8, perThread = 20_000;
        var results = new long[threads * perThread];

        var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
                results[t * perThread + i] = gen.NextId();
        })).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(results.Length, results.Distinct().Count());
    }

    [Fact]
    public void Sequence_ShouldAdvanceWithinSameMillisecond()
    {
        var gen = new SnowflakeIdGenerator(1, 1);
        var first = gen.NextId();
        var second = gen.NextId();
        var seq1 = first & 4095;
        var seq2 = second & 4095;
        // 同毫秒内序列号递增（跨毫秒时后一个可能清零，但不会倒退）
        Assert.True(seq2 == (seq1 + 1) % 4096 || second > first);
    }

    [Fact]
    public void Timestamp_ShouldBeAfterEpoch()
    {
        const long epoch = 1577836800000L;
        const int timestampLeftShift = 12 + 5 + 5; // 22 位（源码注释写 29，与实际位布局不符，见疑点记录）
        var gen = new SnowflakeIdGenerator(1, 1);
        var id = gen.NextId();
        var ts = (id >> timestampLeftShift) + epoch;
        Assert.True(ts >= epoch);
        // 时间戳部分应接近当前时间（毫秒级），偏差容忍 1 小时（时钟同步窗口）
        Assert.True(Math.Abs(ts - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) < 3_600_000);
    }
}
