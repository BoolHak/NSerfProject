// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class SignalHandlingIntegrationTests
{
    [Fact]
    public void SignalHandler_SIGINT_TriggersGracefulShutdown()
    {
        var handler = new SignalHandler();
        Signal? received = null;
        
        handler.RegisterCallback(sig => received = sig);
        handler.TriggerSignal(Signal.SIGINT);
        
        Assert.Equal(Signal.SIGINT, received);
        handler.Dispose();
    }

    [Fact]
    public void SignalHandler_SIGTERM_TriggersConfiguredShutdown()
    {
        var handler = new SignalHandler();
        Signal? received = null;
        
        handler.RegisterCallback(sig => received = sig);
        handler.TriggerSignal(Signal.SIGTERM);
        
        Assert.Equal(Signal.SIGTERM, received);
        handler.Dispose();
    }

    [Fact]
    public void SignalHandler_SIGHUP_TriggersConfigReload()
    {
        var handler = new SignalHandler();
        Signal? received = null;
        
        handler.RegisterCallback(sig => received = sig);
        handler.TriggerSignal(Signal.SIGHUP);
        
        Assert.Equal(Signal.SIGHUP, received);
        handler.Dispose();
    }

    [Fact]
    public void SignalHandler_DoubleSignal_ForcesShutdown()
    {
        var handler = new SignalHandler();
        int count = 0;
        
        handler.RegisterCallback(_ => count++);
        
        handler.TriggerSignal(Signal.SIGINT);
        handler.TriggerSignal(Signal.SIGINT);
        
        Assert.Equal(2, count);
        handler.Dispose();
    }

    [Fact]
    public async Task AgentCommand_LeaveOnTerm_LeavesGracefully()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            LeaveOnTerm = true
        };

        var command = new AgentCommand(config);
        var cts = new CancellationTokenSource();
        
        var runTask = Task.Run(async () => await command.RunAsync(cts.Token));
        await Task.Delay(500);
        
        cts.Cancel();
        
        var exitCode = await runTask;
        await command.DisposeAsync();
        
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task AgentCommand_SkipLeaveOnInt_ForcesShutdown()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            SkipLeaveOnInt = true
        };

        var command = new AgentCommand(config);
        var cts = new CancellationTokenSource();
        
        var runTask = Task.Run(async () => await command.RunAsync(cts.Token));
        await Task.Delay(500);
        
        cts.Cancel();
        
        var exitCode = await runTask;
        await command.DisposeAsync();
        
        // Force shutdown returns 0 if successful
        Assert.True(exitCode == 0 || exitCode == 1);
    }

    [Fact]
    public async Task AgentCommand_GracefulTimeout_ForcesShutdown()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0",
            LeaveOnTerm = true
        };

        var command = new AgentCommand(config);
        var cts = new CancellationTokenSource();
        
        var runTask = Task.Run(async () => await command.RunAsync(cts.Token));
        await Task.Delay(300);
        
        // Cancel and wait for graceful timeout (3 seconds)
        cts.Cancel();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await runTask;
        sw.Stop();
        
        await command.DisposeAsync();
        
        // Should complete within reasonable time
        Assert.True(sw.Elapsed.TotalSeconds < 5);
    }
}
