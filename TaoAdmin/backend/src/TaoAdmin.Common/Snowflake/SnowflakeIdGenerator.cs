using System;

namespace TaoAdmin.Common.Snowflake;

/// <summary>
/// 雪花ID生成器（单例注册，datacenterId + workerId 可通过配置指定）。
/// 结构：1位符号位 + 41位时间戳（毫秒） + 5位数据中心ID + 5位机器ID + 12位序列号。
/// 需保证同一数据中心内 workerId 唯一，可通过环境变量/配置中心分配。
/// </summary>
public class SnowflakeIdGenerator
{
    /// <summary>起始时间戳：2020-01-01 00:00:00 UTC（毫秒）</summary>
    private const long Epoch = 1577836800000L;

    private const int WorkerIdBits = 5;
    private const int DatacenterIdBits = 5;
    private const int SequenceBits = 12;

    /// <summary>支持的最大机器ID（31）</summary>
    public const long MaxWorkerId = ~(-1L << WorkerIdBits);
    /// <summary>支持的最大数据中心ID（31）</summary>
    public const long MaxDatacenterId = ~(-1L << DatacenterIdBits);

    private const int WorkerIdShift = SequenceBits;                                   // 12
    private const int DatacenterIdShift = SequenceBits + WorkerIdBits;                // 17
    private const int TimestampLeftShift = SequenceBits + WorkerIdBits + DatacenterIdBits; // 29
    private const long SequenceMask = ~(-1L << SequenceBits);                         // 4095

    private readonly object _lock = new();
    private long _lastTimestamp = -1L;
    private long _sequence;

    public long WorkerId { get; }
    public long DatacenterId { get; }

    /// <param name="datacenterId">数据中心ID：0 ~ 31</param>
    /// <param name="workerId">机器ID：0 ~ 31</param>
    public SnowflakeIdGenerator(long datacenterId, long workerId)
    {
        if (datacenterId < 0 || datacenterId > MaxDatacenterId)
            throw new ArgumentException($"datacenterId 必须在 0 ~ {MaxDatacenterId} 之间", nameof(datacenterId));
        if (workerId < 0 || workerId > MaxWorkerId)
            throw new ArgumentException($"workerId 必须在 0 ~ {MaxWorkerId} 之间", nameof(workerId));

        DatacenterId = datacenterId;
        WorkerId = workerId;
    }

    /// <summary>生成全局唯一的长整型ID（线程安全）</summary>
    public long NextId()
    {
        lock (_lock)
        {
            var timestamp = CurrentTimeMillis();

            // 时钟回拨保护：直接拒绝，避免生成重复ID
            if (timestamp < _lastTimestamp)
                throw new InvalidOperationException($"检测到时钟回拨，拒绝生成ID（回拨 {(_lastTimestamp - timestamp)} 毫秒）");

            if (timestamp == _lastTimestamp)
            {
                // 同一毫秒内序列号递增，超出掩码则等待下一毫秒
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                {
                    timestamp = WaitNextMillis(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0L;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - Epoch) << TimestampLeftShift)
                   | (DatacenterId << DatacenterIdShift)
                   | (WorkerId << WorkerIdShift)
                   | _sequence;
        }
    }

    private static long WaitNextMillis(long lastTimestamp)
    {
        var timestamp = CurrentTimeMillis();
        while (timestamp <= lastTimestamp)
        {
            timestamp = CurrentTimeMillis();
        }
        return timestamp;
    }

    private static long CurrentTimeMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
