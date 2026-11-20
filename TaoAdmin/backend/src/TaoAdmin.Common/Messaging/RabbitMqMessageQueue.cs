using System.Collections.Concurrent;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TaoAdmin.Common.Messaging;

/// <summary>
/// RabbitMQ 消息队列实现（【线上】推荐）。
/// 每个主题对应一个 durable 队列，使用默认直连交换机（routing key = 主题名）。
/// 由配置 RabbitMq:Enabled=true 启用；连接参数见 RabbitMq 节。
/// </summary>
public class RabbitMqMessageQueue : IMessageQueue, IDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly string _queuePrefix;
    private readonly ConcurrentDictionary<string, IChannel> _channels = new();
    private IConnection? _connection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public string ProviderName => "rabbitmq";

    public RabbitMqMessageQueue(string hostName, int port, string userName, string password, string virtualHost, string queuePrefix = "taoadmin.")
    {
        _factory = new ConnectionFactory
        {
            HostName = hostName,
            Port = port,
            UserName = userName,
            Password = password,
            VirtualHost = virtualHost,
            AutomaticRecoveryEnabled = true   // 断线自动重连
        };
        _queuePrefix = queuePrefix;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true })
            return _channels.Values.First();
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_connection is not { IsOpen: true })
                _connection = await _factory.CreateConnectionAsync(ct);
            return _channels.Values.FirstOrDefault() ?? await CreateChannelAsync(ct);
        }
        finally { _connectLock.Release(); }
    }

    private async Task<IChannel> CreateChannelAsync(CancellationToken ct)
    {
        var ch = await _connection!.CreateChannelAsync(cancellationToken: ct);
        _channels[Guid.NewGuid().ToString("N")] = ch;
        return ch;
    }

    public async Task PublishAsync(string topic, string body)
    {
        var ct = CancellationToken.None;
        var ch = await GetChannelAsync(ct);
        var queue = _queuePrefix + topic;
        await ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        var props = new BasicProperties { MessageId = Guid.NewGuid().ToString("N"), Persistent = true };
        await ch.BasicPublishAsync(string.Empty, queue, true, props, System.Text.Encoding.UTF8.GetBytes(body), ct);
    }

    public async Task SubscribeAsync(string topic, Func<MessageEnvelope, Task> onMessage, CancellationToken ct)
    {
        var ch = await GetChannelAsync(ct);
        var queue = _queuePrefix + topic;
        await ch.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        var consumer = new AsyncEventingBasicConsumer(ch);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var msg = new MessageEnvelope
                {
                    MessageId = ea.BasicProperties.MessageId ?? Guid.NewGuid().ToString("N"),
                    Topic = topic,
                    Body = System.Text.Encoding.UTF8.GetString(ea.Body.ToArray())
                };
                await onMessage(msg);
                await ch.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "RabbitMQ 消息处理失败 topic={Topic}", topic);
                await ch.BasicNackAsync(ea.DeliveryTag, false, requeue: false); // 进入死信，避免无限重投
            }
        };
        await ch.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken: ct);
    }

    public void Dispose()
    {
        foreach (var ch in _channels.Values) ch.Dispose();
        _connection?.Dispose();
        _connectLock.Dispose();
    }
}
