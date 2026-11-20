using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TaoAdmin.Common.Messaging;

/// <summary>消息信封：跨队列传输的标准消息体</summary>
public class MessageEnvelope
{
    /// <summary>消息唯一编号</summary>
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>主题（如 user.created / order.paid）</summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>消息内容（JSON）</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>发布时间</summary>
    public DateTime PublishedAt { get; set; } = DateTime.Now;

    /// <summary>链路追踪 ID（网关生成，可空）</summary>
    public string? TraceId { get; set; }
}

/// <summary>
/// 消息队列抽象：本地内存实现（开发/单机）与 RabbitMQ 实现（线上）二选一，
/// 由配置 RabbitMq:Enabled 决定，业务代码只依赖本接口。
/// </summary>
public interface IMessageQueue
{
    /// <summary>当前提供方名称（memory / rabbitmq），用于监控展示</summary>
    string ProviderName { get; }

    /// <summary>发布一条消息到指定主题</summary>
    Task PublishAsync(string topic, string body);

    /// <summary>订阅主题：收到消息后回调 onMessage（由后台服务在启动时调用）</summary>
    Task SubscribeAsync(string topic, Func<MessageEnvelope, Task> onMessage, CancellationToken ct);

    /// <summary>队列积压统计（主题 → 待消费条数），实现不支持时返回空</summary>
    Dictionary<string, long> GetStats() => new();
}

/// <summary>消息处理器：每个实现处理一个主题，DI 注册后被后台服务自动订阅</summary>
public interface IMessageHandler
{
    /// <summary>处理的主题</summary>
    string Topic { get; }

    /// <summary>业务处理逻辑</summary>
    Task HandleAsync(MessageEnvelope message);
}

/// <summary>
/// 内存消息队列：基于 Channel 的进程内队列。
/// 【本地】开发环境/单机部署默认使用，无外部依赖；服务重启消息清空。
/// 【线上】RabbitMq:Enabled=true 时自动切换为 RabbitMQ，本类不参与。
/// </summary>
public class MemoryMessageQueue : IMessageQueue
{
    private readonly ConcurrentDictionary<string, Channel<MessageEnvelope>> _channels = new();
    public string ProviderName => "memory";

    public Task PublishAsync(string topic, string body)
    {
        var channel = _channels.GetOrAdd(topic, _ => Channel.CreateBounded<MessageEnvelope>(10000));
        var message = new MessageEnvelope { Topic = topic, Body = body };
        return channel.Writer.WriteAsync(message).AsTask();
    }

    public async Task SubscribeAsync(string topic, Func<MessageEnvelope, Task> onMessage, CancellationToken ct)
    {
        var channel = _channels.GetOrAdd(topic, _ => Channel.CreateBounded<MessageEnvelope>(10000));
        // 后台循环：逐条消费，异常不中断（记录后跳过，避免一条坏消息卡死队列）
        _ = Task.Run(async () =>
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(ct))
            {
                try { await onMessage(msg); }
                catch (Exception ex) { Serilog.Log.Error(ex, "消息处理失败 topic={Topic} msgId={MsgId}", msg.Topic, msg.MessageId); }
            }
        }, ct);
        await Task.CompletedTask;
    }

    public Dictionary<string, long> GetStats()
        => _channels.ToDictionary(kv => kv.Key, kv => (long)kv.Value.Reader.Count);
}
