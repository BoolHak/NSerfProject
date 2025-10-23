// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.Agent;

public class AgentLifecycleTests
{
    [Fact]
    public void Agent_Create_InitializesAgent()
    {
        var config = new AgentConfig
        {
            NodeName = "test-create",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);

        Assert.NotNull(agent);
        Assert.Null(agent.Serf);  // Not started yet
    }

    [Fact]
    public async Task Agent_Start_CreatesSerfInstance()
    {
        var config = new AgentConfig
        {
            NodeName = "test-start",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_StartTwice_ThrowsException()
    {
        var config = new AgentConfig
        {
            NodeName = "test-start-twice",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await agent.StartAsync();
        });

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_Leave_InitiatesGracefulShutdown()
    {
        var config = new AgentConfig
        {
            NodeName = "test-leave",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Leave should initiate graceful shutdown
        await agent.Serf!.LeaveAsync();

        await Task.Delay(200);  // Give time for leave to process

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_Shutdown_StopsAllProcesses()
    {
        var config = new AgentConfig
        {
            NodeName = "test-shutdown",
            BindAddr = "127.0.0.1:0",
            RPCAddr = "127.0.0.1:0"  // Use dynamic port
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        await agent.ShutdownAsync();

        // Serf should be stopped
        Assert.Null(agent.Serf);
    }

    [Fact]
    public async Task Agent_ShutdownIdempotent_CanCallMultipleTimes()
    {
        var config = new AgentConfig
        {
            NodeName = "test-shutdown-idempotent",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        await agent.ShutdownAsync();
        await agent.ShutdownAsync();  // Should not throw
        await agent.ShutdownAsync();  // Should not throw

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ShutdownBeforeLeave_Works()
    {
        var config = new AgentConfig
        {
            NodeName = "test-shutdown-before-leave",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Shutdown without leave
        await agent.ShutdownAsync();

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ShutdownBeforeStart_Works()
    {
        var config = new AgentConfig
        {
            NodeName = "test-shutdown-no-start",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);

        // Shutdown without starting
        await agent.ShutdownAsync();

        await agent.DisposeAsync();
    }
}
