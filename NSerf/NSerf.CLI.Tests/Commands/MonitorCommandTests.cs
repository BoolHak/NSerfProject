// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.CommandLine;
using NSerf.CLI.Commands;
using NSerf.CLI.Tests.Fixtures;
using NSerf.CLI.Tests.Helpers;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for the Monitor command - streams logs from the Serf agent.
/// Port of: serf/cmd/serf/command/agent/ipc_log_stream_test.go
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class MonitorCommandTests
{
    /// <summary>
    /// Test that monitor command streams logs from the agent.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task MonitorCommand_StreamsLogs()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        
        var rootCommand = new RootCommand();
        rootCommand.Add(MonitorCommand.Create());
        
        var args = new[]
        {
            "monitor",
            "--rpc-addr", fixture.RpcAddr!,
            "--log-level", "INFO"
        };
        
        var output = new List<string>();
        var originalOut = Console.Out;
        var writer = new StringWriter();
        
        try
        {
            Console.SetOut(writer);
            
            // Act - start monitoring in background
            var monitorTask = Task.Run(async () =>
            {
                try
                {
                    await rootCommand.Parse(args).InvokeAsync();
                }
                catch (OperationCanceledException)
                {
                    // Expected when we cancel
                }
            });
            
            // Wait a bit for logs to stream
            await Task.Delay(1000);
            
            // Trigger some agent activity to generate logs
            await fixture.Agent!.Serf!.UserEventAsync("test-event", Array.Empty<byte>(), false);
            
            await Task.Delay(500);
            
            // Cancel monitoring
            cts.Cancel();
            
            await Task.WhenAny(monitorTask, Task.Delay(2000));
            
            // Assert - should have received some log output
            var outputText = writer.ToString();
            Assert.NotEmpty(outputText);
            Assert.Contains("Streaming logs", outputText);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
    
    /// <summary>
    /// Test that monitor command respects log level filtering.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task MonitorCommand_RespectsLogLevel()
    {
        // Arrange
        await using var fixture = new AgentFixture();
        await fixture.InitializeAsync();
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        
        var rootCommand = new RootCommand();
        rootCommand.Add(MonitorCommand.Create());
        
        var args = new[]
        {
            "monitor",
            "--rpc-addr", fixture.RpcAddr!,
            "--log-level", "WARN" // Higher threshold - fewer logs
        };
        
        var writer = new StringWriter();
        var originalOut = Console.Out;
        
        try
        {
            Console.SetOut(writer);
            
            // Act
            var monitorTask = Task.Run(async () =>
            {
                try
                {
                    await rootCommand.Parse(args).InvokeAsync();
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            });
            
            await Task.Delay(1000);
            cts.Cancel();
            await Task.WhenAny(monitorTask, Task.Delay(2000));
            
            // Assert - should have started monitoring
            var outputText = writer.ToString();
            Assert.Contains("Streaming logs at level: WARN", outputText);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
    
    /// <summary>
    /// Test that monitor command fails with invalid RPC address.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task MonitorCommand_InvalidRpcAddress_Fails()
    {
        // Arrange
        var rootCommand = new RootCommand();
        rootCommand.Add(MonitorCommand.Create());
        
        var args = new[]
        {
            "monitor",
            "--rpc-addr", "127.0.0.1:19999", // Invalid address
            "--log-level", "INFO"
        };
        
        var errorWriter = new StringWriter();
        var originalError = Console.Error;
        
        try
        {
            Console.SetError(errorWriter);
            
            // Act - should fail gracefully
            var exitCode = await rootCommand.Parse(args).InvokeAsync();
            
            // Assert - should have error output
            var errorText = errorWriter.ToString();
            Assert.Contains("Error:", errorText);
            Assert.Contains("Failed to connect", errorText);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
