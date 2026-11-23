using SqlSugar;
using TaoAdmin.Application.Dtos;
using TaoAdmin.Application.Services;
using TaoAdmin.Common.Snowflake;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// 服务下线告警评估器测试：状态迁移计数（首次 DOWN / 连续 DOWN / 恢复清零）与告警触发参数。
/// </summary>
public class ServiceDownAlertEvaluatorTests
{
    private static ServiceItemDto Service(string name, string status, string display = "")
        => new()
        {
            serviceName = name,
            displayName = display == "" ? name : display,
            status = status
        };

    [Fact]
    public async Task EvaluateAsync_FirstDown_ShouldNotifyWithCount1()
    {
        var calls = new List<(string EventType, string Title, string Content, int Count)>();
        var evaluator = new ServiceDownAlertEvaluator((t, c, m, n) =>
        {
            calls.Add((t, c, m, n));
            return Task.FromResult(1);
        });

        await evaluator.EvaluateAsync(new[] { Service("taoadmin-gateway", "DOWN") });

        var call = Assert.Single(calls);
        Assert.Equal("SERVICE_DOWN", call.EventType);
        Assert.Contains("taoadmin-gateway", call.Title);
        Assert.Contains("taoadmin-gateway", call.Content);
        Assert.Equal(1, call.Count);
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedDown_ShouldIncrementCount()
    {
        var calls = new List<int>();
        var evaluator = new ServiceDownAlertEvaluator((_, _, _, n) =>
        {
            calls.Add(n);
            return Task.FromResult(1);
        });

        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN") });
        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN") });
        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN") });

        Assert.Equal(new[] { 1, 2, 3 }, calls);
    }

    [Fact]
    public async Task EvaluateAsync_RecoveredThenDownAgain_ShouldRestartCount()
    {
        var calls = new List<int>();
        var evaluator = new ServiceDownAlertEvaluator((_, _, _, n) =>
        {
            calls.Add(n);
            return Task.FromResult(1);
        });

        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN") });
        await evaluator.EvaluateAsync(new[] { Service("gw", "UP") });
        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN") });

        Assert.Equal(new[] { 1, 1 }, calls);
    }

    [Fact]
    public async Task EvaluateAsync_UpService_ShouldNotNotify()
    {
        var calls = new List<int>();
        var evaluator = new ServiceDownAlertEvaluator((_, _, _, n) =>
        {
            calls.Add(n);
            return Task.FromResult(1);
        });

        await evaluator.EvaluateAsync(new[] { Service("gw", "UP"), Service("id", "UP") });

        Assert.Empty(calls);
    }

    [Fact]
    public async Task EvaluateAsync_MixedServices_ShouldCountIndependently()
    {
        var calls = new List<(string, int)>();
        var evaluator = new ServiceDownAlertEvaluator((_, title, _, n) =>
        {
            calls.Add((title, n));
            return Task.FromResult(1);
        });

        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN"), Service("id", "UP") });
        await evaluator.EvaluateAsync(new[] { Service("gw", "DOWN"), Service("id", "DOWN") });

        // gw 连续两轮 DOWN 各通知一次（计数 1、2），id 第二轮才 DOWN（计数 1）
        Assert.Equal(3, calls.Count);
        Assert.Equal(new[] { 1, 2, 1 }, calls.Select(c => c.Item2));
        Assert.Contains("gw", calls[0].Item1);
        Assert.Contains("gw", calls[1].Item1);
        Assert.Contains("id", calls[2].Item1);
    }
}
