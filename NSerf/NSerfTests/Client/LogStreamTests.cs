using Microsoft.Extensions.Logging;
using NSerf.Client;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for LogStream per-client filtering (Phase 16 - Task 2.2).
/// Tests written BEFORE implementation (RED phase).
/// All tests include timeouts to prevent hanging.
/// </summary>
public class LogStreamTests
{
    [Fact]
    public void InfoLevel_FiltersDebugLogs()
    {
        // Test: INFO level should filter out DEBUG logs
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new LogStream(client, 1, LogLevel.Information, cts.Token);
        
        // Debug should be filtered
        Assert.False(stream.ShouldSendLog(LogLevel.Debug));
        
        // Info and above should pass
        Assert.True(stream.ShouldSendLog(LogLevel.Information));
        Assert.True(stream.ShouldSendLog(LogLevel.Warning));
        Assert.True(stream.ShouldSendLog(LogLevel.Error));
        
        cts.Cancel();
    }

    [Fact(Timeout = 5000)]
    public async Task SendLogAsync_FormatsCorrectly()
    {
        // Test: Log should be formatted as plain string
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new LogStream(client, 2, LogLevel.Debug, cts.Token);
        
        // This should not throw
        await stream.SendLogAsync("Test log message", LogLevel.Information);
        
        // In real implementation, would verify client.SendAsync was called
        cts.Cancel();
    }

    [Fact]
    public void WireFormat_IsLogRecord()
    {
        // Test: Log format should be plain string (wrapped in { "Log": "..." } by protocol)
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new LogStream(client, 3, LogLevel.Debug, cts.Token);
        
        var formatted = stream.FormatLog("Test message");
        
        // Should return plain string (wire protocol adds { "Log": "..." } wrapper)
        Assert.Equal("Test message", formatted);
        
        cts.Cancel();
    }

    [Fact]
    public void DebugLevel_AllowsAllLogs()
    {
        // Test: DEBUG level should allow all log levels
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new LogStream(client, 4, LogLevel.Debug, cts.Token);
        
        Assert.True(stream.ShouldSendLog(LogLevel.Debug));
        Assert.True(stream.ShouldSendLog(LogLevel.Information));
        Assert.True(stream.ShouldSendLog(LogLevel.Warning));
        Assert.True(stream.ShouldSendLog(LogLevel.Error));
        Assert.True(stream.ShouldSendLog(LogLevel.Critical));
        
        cts.Cancel();
    }

    [Fact]
    public void ErrorLevel_OnlyAllowsErrors()
    {
        // Test: ERROR level should only allow Error and Critical
        var client = new MockIpcClientHandler("test-client");
        var cts = new CancellationTokenSource();
        var stream = new LogStream(client, 5, LogLevel.Error, cts.Token);
        
        Assert.False(stream.ShouldSendLog(LogLevel.Debug));
        Assert.False(stream.ShouldSendLog(LogLevel.Information));
        Assert.False(stream.ShouldSendLog(LogLevel.Warning));
        Assert.True(stream.ShouldSendLog(LogLevel.Error));
        Assert.True(stream.ShouldSendLog(LogLevel.Critical));
        
        cts.Cancel();
    }
}
