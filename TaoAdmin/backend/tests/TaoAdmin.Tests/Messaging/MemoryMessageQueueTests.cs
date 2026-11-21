using TaoAdmin.Common.Messaging;
using Xunit;

namespace TaoAdmin.Tests.Messaging;

/// <summary>内存消息队列：发布/订阅、坏消息隔离、积压统计（纯逻辑，无外部依赖）</summary>
public class MemoryMessageQueueTests
{
    [Fact]
    public async Task PublishThenSubscribe_ShouldDeliverEnvelope()
    {
        var queue = new MemoryMessageQueue();
        var received = new TaskCompletionSource<MessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        await queue.SubscribeAsync("user.created", msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        }, CancellationToken.None);

        await queue.PublishAsync("user.created", "{\"id\":1}");

        var delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("user.created", delivered.Topic);
        Assert.Equal("{\"id\":1}", delivered.Body);
        Assert.False(string.IsNullOrEmpty(delivered.MessageId));
    }

    [Fact]
    public async Task HandlerException_ShouldNotBlockFollowingMessages()
    {
        // 一条坏消息处理失败不中断队列，后续消息照常投递
        var queue = new MemoryMessageQueue();
        var second = new TaskCompletionSource<MessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;

        await queue.SubscribeAsync("order.paid", _ =>
        {
            if (Interlocked.Increment(ref count) == 1) throw new InvalidOperationException("坏消息");
            second.TrySetResult(null!);
            return Task.CompletedTask;
        }, CancellationToken.None);

        await queue.PublishAsync("order.paid", "bad");
        await queue.PublishAsync("order.paid", "good");

        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetStats_ShouldReportBacklogPerTopic()
    {
        // 无订阅者时消息留在通道内，积压按主题统计（无消费竞态，口径确定）
        var queue = new MemoryMessageQueue();
        await queue.PublishAsync("t1", "m1");
        await queue.PublishAsync("t1", "m2");
        await queue.PublishAsync("t2", "m3");

        var stats = queue.GetStats();
        Assert.Equal(2, stats["t1"]);
        Assert.Equal(1, stats["t2"]);
    }

    [Fact]
    public async Task TwoTopics_ShouldStayIsolated()
    {
        var queue = new MemoryMessageQueue();
        var gotA = new TaskCompletionSource<MessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gotB = new TaskCompletionSource<MessageEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);

        await queue.SubscribeAsync("a", m => { gotA.TrySetResult(m); return Task.CompletedTask; }, CancellationToken.None);
        await queue.SubscribeAsync("b", m => { gotB.TrySetResult(m); return Task.CompletedTask; }, CancellationToken.None);

        await queue.PublishAsync("b", "for-b");
        var delivered = await gotB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("b", delivered.Topic);
        Assert.False(gotA.Task.IsCompleted); // 主题 A 不收到 B 的消息
    }
}
