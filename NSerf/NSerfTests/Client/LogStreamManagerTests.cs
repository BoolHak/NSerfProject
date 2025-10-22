using Microsoft.Extensions.Logging;
using NSerf.Client;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for LogStreamManager (Phase 16 - Task 1.2).
/// Tests written BEFORE implementation (RED phase).
/// All tests include timeouts to prevent hanging.
/// </summary>
public class LogStreamManagerTests : IAsyncLifetime
{
    private LogStreamManager _manager = null!;
    private MockLogger _mockLogger = null!;
    private CancellationTokenSource _cts = null!;

    public Task InitializeAsync()
    {
        _mockLogger = new MockLogger();
        _manager = new LogStreamManager(_mockLogger);
        _cts = new CancellationTokenSource();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        _cts.Dispose();
        await Task.CompletedTask;
    }

    [Fact(Timeout = 5000)]
    public async Task CapturesLogOutput_FromSerfLogger()
    {
        // Test: LogStreamManager should capture logs from the wrapped logger
        var client = new MockIpcClientHandler("test-client");
        var receivedLogs = new List<string>();
        
        _manager.RegisterMonitor(1, client, "debug", receivedLogs, _cts.Token);
        
        // Emit a log through the mock logger
        _mockLogger.EmitLog(LogLevel.Information, "Test log message");
        
        await Task.Delay(100);
        
        // Should have captured the log
        Assert.Single(receivedLogs);
        Assert.Contains("Test log message", receivedLogs[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task FiltersByLogLevel_DebugInfoWarnError()
    {
        // Test: Log level filtering - INFO level should not receive DEBUG logs
        var client = new MockIpcClientHandler("test-client");
        var receivedLogs = new List<string>();
        
        _manager.RegisterMonitor(2, client, "info", receivedLogs, _cts.Token);
        
        // Emit logs at different levels
        _mockLogger.EmitLog(LogLevel.Debug, "Debug message");      // Should be filtered
        _mockLogger.EmitLog(LogLevel.Information, "Info message"); // Should pass
        _mockLogger.EmitLog(LogLevel.Warning, "Warn message");     // Should pass
        _mockLogger.EmitLog(LogLevel.Error, "Error message");      // Should pass
        
        await Task.Delay(100);
        
        // Should have 3 logs (Info, Warn, Error - not Debug)
        Assert.Equal(3, receivedLogs.Count);
        Assert.Contains("Info message", receivedLogs[0]);
        Assert.Contains("Warn message", receivedLogs[1]);
        Assert.Contains("Error message", receivedLogs[2]);
    }

    [Fact(Timeout = 5000)]
    public async Task MultipleMonitors_AllReceiveLogs()
    {
        // Test: Multiple monitors should all receive logs independently
        var client1 = new MockIpcClientHandler("client1");
        var client2 = new MockIpcClientHandler("client2");
        var receivedLogs1 = new List<string>();
        var receivedLogs2 = new List<string>();
        
        _manager.RegisterMonitor(3, client1, "debug", receivedLogs1, _cts.Token);
        _manager.RegisterMonitor(4, client2, "debug", receivedLogs2, _cts.Token);
        
        _mockLogger.EmitLog(LogLevel.Information, "Broadcast message");
        
        await Task.Delay(100);
        
        // Both should have received it
        Assert.Single(receivedLogs1);
        Assert.Single(receivedLogs2);
        Assert.Contains("Broadcast message", receivedLogs1[0]);
        Assert.Contains("Broadcast message", receivedLogs2[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task LogFormat_MatchesGoProtocol()
    {
        // Test: Log format should match Go's { "Log": "string" } format
        var client = new MockIpcClientHandler("test-client");
        var receivedLogs = new List<string>();
        
        _manager.RegisterMonitor(5, client, "debug", receivedLogs, _cts.Token);
        
        _mockLogger.EmitLog(LogLevel.Information, "Formatted log");
        
        await Task.Delay(100);
        
        Assert.Single(receivedLogs);
        // Log should be plain string (will be wrapped in { "Log": "..." } by wire protocol)
        Assert.Equal("Formatted log", receivedLogs[0]);
    }

    [Fact(Timeout = 5000)]
    public async Task UnregisterMonitor_StopsReceivingLogs()
    {
        // Test: Unregistering should stop receiving logs
        var client = new MockIpcClientHandler("test-client");
        var receivedLogs = new List<string>();
        
        _manager.RegisterMonitor(6, client, "debug", receivedLogs, _cts.Token);
        
        _mockLogger.EmitLog(LogLevel.Information, "First log");
        await Task.Delay(100);
        
        Assert.Single(receivedLogs);
        
        // Unregister
        _manager.UnregisterMonitor(6);
        
        _mockLogger.EmitLog(LogLevel.Information, "Second log");
        await Task.Delay(100);
        
        // Should still only have 1 log
        Assert.Single(receivedLogs);
    }
}

/// <summary>
/// Mock logger for testing log capture.
/// </summary>
internal class MockLogger : ILogger, NSerf.Client.ILoggableLogger
{
    private readonly List<Action<LogLevel, string>> _logHandlers = new();

    public void AddLogHandler(Action<LogLevel, string> handler)
    {
        lock (_logHandlers)
        {
            _logHandlers.Add(handler);
        }
    }

    public void EmitLog(LogLevel level, string message)
    {
        List<Action<LogLevel, string>> handlers;
        lock (_logHandlers)
        {
            handlers = _logHandlers.ToList();
        }

        foreach (var handler in handlers)
        {
            handler(level, message);
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        EmitLog(logLevel, message);
    }
}
