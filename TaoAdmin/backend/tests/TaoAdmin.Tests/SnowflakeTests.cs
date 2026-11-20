using TaoAdmin.Common.Snowflake;
using Xunit;

namespace TaoAdmin.Tests;

/// <summary>雪花ID：唯一性与趋势递增</summary>
public class SnowflakeTests
{
    [Fact]
    public void NextId_ShouldBeUnique()
    {
        var gen = new SnowflakeIdGenerator(1, 1);
        var ids = new HashSet<long>();
        for (var i = 0; i < 100_000; i++)
            Assert.True(ids.Add(gen.NextId()));
    }

    [Fact]
    public void NextId_ShouldBeMonotonic()
    {
        var gen = new SnowflakeIdGenerator(1, 1);
        long last = 0;
        for (var i = 0; i < 10_000; i++)
        {
            var id = gen.NextId();
            Assert.True(id > last);
            last = id;
        }
    }

    [Fact]
    public void NextId_MultiWorker_ShouldNotCollide()
    {
        var a = new SnowflakeIdGenerator(1, 1);
        var b = new SnowflakeIdGenerator(1, 2);
        var ids = new HashSet<long>();
        for (var i = 0; i < 10_000; i++)
        {
            Assert.True(ids.Add(a.NextId()));
            Assert.True(ids.Add(b.NextId()));
        }
    }
}
