using TaoAdmin.Application.Services;
using Xunit;

namespace TaoAdmin.Tests.Application;

/// <summary>
/// Serilog 日志行解析器：@m 已渲染格式、@mt 模板+参数格式、非 JSON 行、服务来源回退。
/// 样例行取自各服务真实日志（CompactJsonFormatter 写 @mt + 命名参数）。
/// </summary>
public class SerilogLineParserTests
{
    [Fact]
    public void Parse_RenderedLine_UsesRenderedMessage()
    {
        var line = "{\"@t\":\"2026-11-22T10:00:00.0000000Z\",\"@m\":\"hello world\",\"@l\":\"Information\"}";
        var entry = SerilogLineParser.Parse(line);
        Assert.NotNull(entry);
        Assert.Equal("hello world", entry!.Message);
        Assert.Equal("INFO", entry.Level);
    }

    [Fact]
    public void Parse_TemplateLine_RendersNamedParameter()
    {
        // 真实样例：gateway.jsonl 中 DataProtection 日志（@mt + FullName 命名参数）
        var line = "{\"@t\":\"2026-11-03T18:39:42.8577800Z\",\"@mt\":\"User profile is available. Using '{FullName}' as key repository; keys will not be encrypted at rest.\",\"FullName\":\"/root/.aspnet/DataProtection-Keys\",\"SourceContext\":\"Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager\",\"Service\":\"taoadmin-gateway\"}";
        var entry = SerilogLineParser.Parse(line);
        Assert.NotNull(entry);
        Assert.Equal("User profile is available. Using '/root/.aspnet/DataProtection-Keys' as key repository; keys will not be encrypted at rest.", entry!.Message);
        Assert.Equal("taoadmin-gateway", entry.Service);
        Assert.Equal("Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager", entry.Logger);
    }

    [Fact]
    public void Parse_TemplateLine_MissingParameterKeepsPlaceholder()
    {
        var line = "{\"@t\":\"2026-11-22T10:00:00.0000000Z\",\"@mt\":\"Job {JobId} done\",\"@l\":\"Warning\"}";
        var entry = SerilogLineParser.Parse(line);
        Assert.NotNull(entry);
        Assert.Equal("Job {JobId} done", entry!.Message);
        Assert.Equal("WARN", entry.Level);
    }

    [Fact]
    public void Parse_TemplateWithFormatSpecifier_RendersValue()
    {
        var line = "{\"@t\":\"2026-11-22T10:00:00.0000000Z\",\"@mt\":\"Retry in {RetryCount} s\",\"RetryCount\":5}";
        var entry = SerilogLineParser.Parse(line);
        Assert.NotNull(entry);
        Assert.Equal("Retry in 5 s", entry!.Message);
    }

    [Fact]
    public void Parse_NonJsonLine_ReturnsNull()
    {
        Assert.Null(SerilogLineParser.Parse("not a json line"));
        Assert.Null(SerilogLineParser.Parse(""));
        Assert.Null(SerilogLineParser.Parse("{\"no_timestamp\":true}"));
    }

    [Fact]
    public void Parse_ExceptionAndTraceIdCarried()
    {
        var line = "{\"@t\":\"2026-11-22T10:00:00.0000000Z\",\"@mt\":\"boom\",\"@x\":\"System.Exception: boom\",\"TraceId\":\"abc123\",\"ThreadId\":\"7\"}";
        var entry = SerilogLineParser.Parse(line);
        Assert.NotNull(entry);
        Assert.Equal("System.Exception: boom", entry!.Exception);
        Assert.Equal("abc123", entry.TraceId);
        Assert.Equal("7", entry.Thread);
    }
}
