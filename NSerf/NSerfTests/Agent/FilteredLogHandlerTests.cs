// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using NSerf.Agent;
using NSerf.Agent.RPC;
using Xunit;

namespace NSerfTests.Agent;

public class FilteredLogHandlerTests
{
    private sealed class CollectingHandler : CircularLogWriter.ILogHandler
    {
        public readonly ConcurrentQueue<string> Lines = new();
        public void HandleLog(string log) => Lines.Enqueue(log);
    }

    [Fact]
    public void FilteredLogHandler_FiltersBasedOnLevel()
    {
        var sink = new CollectingHandler();
        var handler = new FilteredLogHandler(sink, LogLevel.Warn);

        handler.HandleLog("[TRACE] trace");
        handler.HandleLog("[DEBUG] debug");
        handler.HandleLog("[INFO] info");
        handler.HandleLog("[WARN] warn");
        handler.HandleLog("[ERR] err");

        var output = sink.Lines.ToArray();
        Assert.DoesNotContain(output, l => l.Contains("TRACE"));
        Assert.DoesNotContain(output, l => l.Contains("DEBUG"));
        Assert.DoesNotContain(output, l => l.Contains("INFO"));
        Assert.Contains(output, l => l.Contains("WARN"));
        Assert.Contains(output, l => l.Contains("ERR"));
    }

    [Fact]
    public void FilteredLogHandler_AllowsUnprefixedLines()
    {
        var sink = new CollectingHandler();
        var handler = new FilteredLogHandler(sink, LogLevel.Error);

        handler.HandleLog("no prefix line");

        var output = sink.Lines.ToArray();
        Assert.Single(output);
        Assert.Contains("no prefix line", output[0]);
    }

    [Fact]
    public void CircularLogWriter_Backlog_IsFilteredByHandler()
    {
        var writer = new CircularLogWriter(bufferSize: 4);

        // write some backlog
        writer.WriteLine("[INFO] startup");
        writer.WriteLine("[DEBUG] init done");
        writer.WriteLine("unprefixed");

        var sink = new CollectingHandler();
        var filtered = new FilteredLogHandler(sink, LogLevel.Info);

        // when registered, backlog is sent first, then live
        writer.RegisterHandler(filtered);
        writer.WriteLine("[ERR] fatal");

        var lines = sink.Lines.ToArray();
        // Expect: [INFO] startup, unprefixed, [ERR] fatal (but not DEBUG)
        Assert.Contains(lines, l => l.Contains("[INFO] startup"));
        Assert.Contains(lines, l => l.Contains("unprefixed"));
        Assert.Contains(lines, l => l.Contains("[ERR] fatal"));
        Assert.DoesNotContain(lines, l => l.Contains("[DEBUG]"));
    }
}
