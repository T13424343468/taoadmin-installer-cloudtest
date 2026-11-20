using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TaoAdmin.Common.Messaging;

/// <summary>
/// 消息队列装配与后台订阅服务。
/// 用法：
///   1. AddMessageQueue(builder.Configuration) —— 按配置选择内存/RabbitMQ 实现；
///   2. 业务方实现 IMessageHandler 并注册 services.AddSingleton&lt;IMessageHandler, XxxHandler&gt;();
///   3. 发布：注入 IMessageQueue 后 PublishAsync(topic, json)。
/// </summary>
public static class MessageQueueSetup
{
    public static IServiceCollection AddMessageQueue(this IServiceCollection services, IConfiguration configuration, bool addPump = true)
    {
        var enabled = configuration.GetValue<bool>("RabbitMq:Enabled");
        IMessageQueue mq = enabled
            ? new RabbitMqMessageQueue(
                configuration["RabbitMq:HostName"] ?? "localhost",
                configuration.GetValue<int>("RabbitMq:Port", 5672),
                configuration["RabbitMq:UserName"] ?? "guest",
                configuration["RabbitMq:Password"] ?? "guest",
                configuration["RabbitMq:VirtualHost"] ?? "/")
            : new MemoryMessageQueue();

        services.AddSingleton(mq);
        if (Environment.GetEnvironmentVariable("MQPUMP") != "0")
            services.AddHostedService<MessageQueuePump>();
        return services;
    }
}

/// <summary>后台订阅泵：把所有 IMessageHandler 注册到队列，随服务启动常驻消费</summary>
public class MessageQueuePump : BackgroundService
{
    private readonly IMessageQueue _mq;
    private readonly IEnumerable<IMessageHandler> _handlers;
    private readonly ILogger<MessageQueuePump> _logger;

    public MessageQueuePump(IMessageQueue mq, IEnumerable<IMessageHandler> handlers, ILogger<MessageQueuePump> logger)
    {
        _mq = mq; _logger = logger;
        _handlers = handlers.ToList();
        logger.LogInformation("消息泵构造完成：{Count} 个处理器", _handlers.Count());
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("消息泵启动中：提供方 {Provider}", _mq.ProviderName);
        var handlerList = _handlers.ToList();
        _logger.LogInformation("消息队列已启动：提供方 {Provider}，订阅主题 {Topics}",
            _mq.ProviderName, string.Join(", ", handlerList.Select(h => h.Topic).Distinct()));
        foreach (var handler in handlerList)
        {
            _ = _mq.SubscribeAsync(handler.Topic, handler.HandleAsync, stoppingToken);
        }
        return Task.CompletedTask;
    }
}
