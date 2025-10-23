// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class AgentCommandTests
{
    [Fact]
    public async Task AgentCommand_StartsWithConfig()
    {
        var config = new AgentConfig
        {
            NodeName = "test-node",
            BindAddr = "127.0.0.1:0"
        };

        var command = new AgentCommand(config);
        
        // Start in background
        var runTask = Task.Run(async () => await command.RunAsync());
        
        // Give it time to start
        await Task.Delay(500);
        
        // Cleanup
        await command.DisposeAsync();
        
        await runTask;
    }

    [Fact]
    public async Task AgentCommand_GracefulShutdown_ReturnsZero()
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
        
        // Trigger graceful shutdown
        cts.Cancel();
        
        var exitCode = await runTask;
        
        await command.DisposeAsync();
        
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void AgentCommand_ConfigValidation_ThrowsOnInvalidConfig()
    {
        var config = new AgentConfig
        {
            Tags = new Dictionary<string, string> { ["test"] = "value" },
            TagsFile = "tags.json"  // Mutual exclusion
        };

        Assert.Throws<ConfigException>(() => new SerfAgent(config));
    }
}
