using System;
using System.Threading;

namespace MasterIM.Server.Storage;

/// <summary>
/// 消息ID生成器（时间戳 + 序列号）
/// </summary>
public class MsgIdGenerator
{
    private long _lastTimestamp = 0;
    private long _sequence = 0;
    private readonly object _lock = new object();

    // 基准时间（2024-01-01 00:00:00 UTC）
    private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // 序列号位数（22位 = 每毫秒最多 4,194,304 条）
    private const int SequenceBits = 22;
    private const long SequenceMask = (1L << SequenceBits) - 1;

    /// <summary>
    /// 生成下一个消息ID
    /// </summary>
    public long NextId()
    {
        lock (_lock)
        {
            var now = GetCurrentTimestamp();

            if (now == _lastTimestamp)
            {
                // 同一毫秒内，序列号递增
                _sequence = (_sequence + 1) & SequenceMask;

                if (_sequence == 0)
                {
                    // 序列号用尽，等待下一毫秒
                    now = WaitNextMillis(_lastTimestamp);
                }
            }
            else if (now < _lastTimestamp)
            {
                // 时钟回拨，抛出异常
                throw new InvalidOperationException(
                    $"Clock moved backwards. Refusing to generate id for {_lastTimestamp - now} milliseconds");
            }
            else
            {
                // 新的毫秒，重置序列号
                _sequence = 0;
            }

            _lastTimestamp = now;

            // 组合：时间戳(42位) << 22 | 序列号(22位)
            return (now << SequenceBits) | _sequence;
        }
    }

    /// <summary>
    /// 从 MsgId 提取时间戳
    /// </summary>
    public static DateTime ExtractDateTime(long msgId)
    {
        var timestamp = msgId >> SequenceBits;
        return Epoch.AddMilliseconds(timestamp);
    }

    private long GetCurrentTimestamp()
    {
        return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;
    }

    private long WaitNextMillis(long lastTimestamp)
    {
        var timestamp = GetCurrentTimestamp();
        while (timestamp <= lastTimestamp)
        {
            Thread.Sleep(0);  // 让出 CPU
            timestamp = GetCurrentTimestamp();
        }
        return timestamp;
    }
}
