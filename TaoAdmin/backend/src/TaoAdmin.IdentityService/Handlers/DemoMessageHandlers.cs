using System.Text.Json;
using TaoAdmin.Common.Messaging;
using TaoAdmin.Common.Snowflake;
using TaoAdmin.Domain.Entities;
using SqlSugar;

namespace TaoAdmin.IdentityService.Handlers;

/// <summary>
/// 【消息队列示例】用户创建事件处理器：
/// 监听 user.created 主题，为新用户生成一条欢迎站内消息落库。
/// </summary>
public class UserCreatedMessageHandler : IMessageHandler
{
    public string Topic => "user.created";
    private readonly ISqlSugarClient _db;
    private readonly SnowflakeIdGenerator _snowflake;
    private readonly ILogger<UserCreatedMessageHandler> _logger;

    public UserCreatedMessageHandler(ISqlSugarClient db, SnowflakeIdGenerator snowflake, ILogger<UserCreatedMessageHandler> logger)
    {
        _db = db; _snowflake = snowflake; _logger = logger;
    }

    public async Task HandleAsync(MessageEnvelope message)
    {
        var payload = JsonSerializer.Deserialize<UserCreatedPayload>(message.Body);
        if (payload == null) return;

        var msg = new SysMessage
        {
            Id = _snowflake.NextId(),
            Title = "欢迎加入 TaoAdmin",
            Content = $"账号 {payload.userName}（{payload.nickname ?? "-"}）创建成功，祝你使用愉快！",
            Topic = message.Topic,
            ReceiverId = payload.userId,
            IsRead = false
        };
        await _db.Insertable(msg).ExecuteCommandAsync();
        _logger.LogInformation("用户创建事件已处理：{User}，欢迎消息 {MsgId} 已生成", payload.userName, msg.Id);
    }

    private class UserCreatedPayload
    {
        public long userId { get; set; }
        public string userName { get; set; } = "";
        public string? nickname { get; set; }
    }
}

/// <summary>
/// 【消息队列示例】演示主题处理器：
/// 监听 demo.test 与 manual 发布的主题（通配由 DI 多注册实现），把消息落库为站内消息。
/// </summary>
public class DemoTopicMessageHandler : IMessageHandler
{
    public string Topic => "demo.test";
    private readonly ISqlSugarClient _db;
    private readonly SnowflakeIdGenerator _snowflake;

    public DemoTopicMessageHandler(ISqlSugarClient db, SnowflakeIdGenerator snowflake) => (_db, _snowflake) = (db, snowflake);

    public async Task HandleAsync(MessageEnvelope message)
    {
        await _db.Insertable(new SysMessage
        {
            Id = _snowflake.NextId(),
            Title = $"【演示】收到消息：{message.Topic}",
            Content = message.Body,
            Topic = message.Topic,
            ReceiverId = 0,
            IsRead = false
        }).ExecuteCommandAsync();
    }
}
