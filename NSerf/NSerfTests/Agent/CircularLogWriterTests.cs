// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class CircularLogWriterTests
{
    private class MockLogHandler : ILogHandler
    {
        public List<string> Logs { get; } = new();
        
        public void HandleLog(string log)
        {
            Logs.Add(log);
        }
    }

    [Fact]
    public void CircularLogWriter_BuffersLogs()
    {
        // Arrange
        var logWriter = new CircularLogWriter(10);
        var handler = new MockLogHandler();
        
        // Act - Write some logs
        logWriter.WriteLine("Log 1");
        logWriter.WriteLine("Log 2");
        logWriter.WriteLine("Log 3");
        
        // Register handler to receive buffered logs
        logWriter.RegisterHandler(handler);
        
        // Assert - Handler should receive all buffered logs
        Assert.Equal(3, handler.Logs.Count);
        Assert.Equal("Log 1", handler.Logs[0]);
        Assert.Equal("Log 2", handler.Logs[1]);
        Assert.Equal("Log 3", handler.Logs[2]);
        
        logWriter.Dispose();
    }

    [Fact]
    public void CircularLogWriter_NewHandler_ReceivesBacklog()
    {
        var logWriter = new CircularLogWriter(10);

        // Write 5 logs
        for (int i = 0; i < 5; i++)
        {
            logWriter.WriteLine($"Log {i}");
        }

        // Register handler
        var receivedLogs = new List<string>();
        var handler = new TestLogHandler(receivedLogs);
        logWriter.RegisterHandler(handler);

        // Should receive backlog
        Assert.Equal(5, receivedLogs.Count);
        Assert.Equal("Log 0", receivedLogs[0]);
        Assert.Equal("Log 4", receivedLogs[4]);

        logWriter.Dispose();
    }

    [Fact]
    public void CircularLogWriter_Wraps_AfterBufferFull()
    {
        var logWriter = new CircularLogWriter(5);

        // Write 8 logs (will wrap)
        for (int i = 0; i < 8; i++)
        {
            logWriter.WriteLine($"Log {i}");
        }

        // Register handler
        var receivedLogs = new List<string>();
        var handler = new TestLogHandler(receivedLogs);
        logWriter.RegisterHandler(handler);

        // Should receive last 5 logs (3-7)
        Assert.Equal(5, receivedLogs.Count);
        Assert.Equal("Log 3", receivedLogs[0]);  // Oldest in buffer
        Assert.Equal("Log 7", receivedLogs[4]);  // Newest in buffer

        logWriter.Dispose();
    }

    [Fact]
    public void CircularLogWriter_NewLog_SentToHandlers()
    {
        var logWriter = new CircularLogWriter(10);

        var receivedLogs = new List<string>();
        var handler = new TestLogHandler(receivedLogs);
        logWriter.RegisterHandler(handler);

        // Write after registration
        logWriter.WriteLine("New Log");

        // Should receive new log
        Assert.Contains("New Log", receivedLogs);

        logWriter.Dispose();
    }

    [Fact]
    public void CircularLogWriter_DeregisterHandler_StopsReceiving()
    {
        var logWriter = new CircularLogWriter(10);

        var receivedLogs = new List<string>();
        var handler = new TestLogHandler(receivedLogs);
        logWriter.RegisterHandler(handler);

        logWriter.WriteLine("Log 1");
        logWriter.DeregisterHandler(handler);
        logWriter.WriteLine("Log 2");

        // Should only have first log
        Assert.Single(receivedLogs);
        Assert.Equal("Log 1", receivedLogs[0]);

        logWriter.Dispose();
    }

    [Fact]
    public void CircularLogWriter_MultipleHandlers_AllReceive()
    {
        var logWriter = new CircularLogWriter(10);

        var logs1 = new List<string>();
        var logs2 = new List<string>();
        var handler1 = new TestLogHandler(logs1);
        var handler2 = new TestLogHandler(logs2);

        logWriter.RegisterHandler(handler1);
        logWriter.RegisterHandler(handler2);

        logWriter.WriteLine("Test");

        Assert.Contains("Test", logs1);
        Assert.Contains("Test", logs2);

        logWriter.Dispose();
    }

    [Fact]
    public void CircularLogWriter_StripsNewline()
    {
        var logWriter = new CircularLogWriter(10);

        var receivedLogs = new List<string>();
        var handler = new TestLogHandler(receivedLogs);
        logWriter.RegisterHandler(handler);

        logWriter.WriteLine("Log with newline\n");

        Assert.Single(receivedLogs);
        Assert.Equal("Log with newline", receivedLogs[0]);

        logWriter.Dispose();
    }
}

public class TestLogHandler : ILogHandler
{
    private readonly List<string> _logs;

    public TestLogHandler(List<string> logs)
    {
        _logs = logs;
    }

    public void HandleLog(string log)
    {
        _logs.Add(log);
    }
}
