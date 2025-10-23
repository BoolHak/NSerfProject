// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class GatedWriterTests
{
    [Fact]
    public void GatedWriter_BuffersUntilFlush()
    {
        var output = new StringWriter();
        var gated = new GatedWriter(output);

        gated.WriteLine("Line 1");
        gated.WriteLine("Line 2");

        Assert.Equal("", output.ToString());

        gated.Flush();

        var result = output.ToString();
        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
    }

    [Fact]
    public void GatedWriter_PassesThroughAfterFlush()
    {
        var output = new StringWriter();
        var gated = new GatedWriter(output);

        gated.WriteLine("Before flush");
        gated.Flush();
        
        var beforeFlush = output.ToString();
        Assert.Contains("Before flush", beforeFlush);

        gated.WriteLine("After flush");
        
        var afterFlush = output.ToString();
        Assert.Contains("After flush", afterFlush);
    }

    [Fact]
    public void GatedWriter_Reset_ClearsBuffer()
    {
        var output = new StringWriter();
        var gated = new GatedWriter(output);

        gated.WriteLine("Buffered line");
        gated.Reset();
        gated.Flush();

        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void GatedWriter_Dispose_FlushesBuffer()
    {
        var output = new StringWriter();
        var gated = new GatedWriter(output);

        gated.WriteLine("Line");
        gated.Dispose();

        Assert.Contains("Line", output.ToString());
    }

    [Fact]
    public void GatedWriter_ThreadSafe_ConcurrentWrites()
    {
        var output = new StringWriter();
        var gated = new GatedWriter(output);

        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            int j = i;
            tasks.Add(Task.Run(() => gated.WriteLine($"Message {j}")));
        }

        Task.WaitAll(tasks.ToArray());
        gated.Flush();

        var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(100, lines.Length);
    }
}
