// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class LogWriterTests
{
    [Fact]
    public void LogWriter_FiltersBasedOnLevel()
    {
        var output = new StringWriter();
        var logWriter = new LogWriter(output, LogLevel.Warn);

        logWriter.WriteLine("[TRACE] Trace message");
        logWriter.WriteLine("[DEBUG] Debug message");
        logWriter.WriteLine("[INFO] Info message");
        logWriter.WriteLine("[WARN] Warning message");
        logWriter.WriteLine("[ERR] Error message");

        var result = output.ToString();
        
        Assert.DoesNotContain("TRACE", result);
        Assert.DoesNotContain("DEBUG", result);
        Assert.DoesNotContain("INFO", result);
        Assert.Contains("WARN", result);
        Assert.Contains("ERR", result);
    }

    [Fact]
    public void LogWriter_AllowsUnprefixedLines()
    {
        var output = new StringWriter();
        var logWriter = new LogWriter(output, LogLevel.Error);

        logWriter.WriteLine("No prefix line");
        
        var result = output.ToString();
        Assert.Contains("No prefix line", result);
    }

    [Theory]
    [InlineData(LogLevel.Trace, 5)]
    [InlineData(LogLevel.Debug, 4)]
    [InlineData(LogLevel.Info, 3)]
    [InlineData(LogLevel.Warn, 2)]
    [InlineData(LogLevel.Error, 1)]
    public void LogWriter_LevelFiltering_CountsCorrectly(LogLevel minLevel, int expectedCount)
    {
        var output = new StringWriter();
        var logWriter = new LogWriter(output, minLevel);

        logWriter.WriteLine("[TRACE] Trace");
        logWriter.WriteLine("[DEBUG] Debug");
        logWriter.WriteLine("[INFO] Info");
        logWriter.WriteLine("[WARN] Warn");
        logWriter.WriteLine("[ERR] Error");

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expectedCount, lines.Length);
    }

    [Fact]
    public void LogWriter_ThreadSafe_ConcurrentWrites()
    {
        var output = new StringWriter();
        var logWriter = new LogWriter(output, LogLevel.Info);

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            int j = i;
            tasks.Add(Task.Run(() => logWriter.WriteLine($"[INFO] Message {j}")));
        }

        Task.WaitAll(tasks.ToArray());
        
        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
    }
}
