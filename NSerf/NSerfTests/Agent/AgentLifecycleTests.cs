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

        // Verify agent is alive
        Assert.Equal(SerfState.SerfAlive, agent.Serf!.State());

        // Leave should initiate graceful shutdown
        await agent.Serf.LeaveAsync();

        // Poll for state transition to Left
        var maxWait = TimeSpan.FromSeconds(2);
        var start = DateTime.UtcNow;
        while (agent.Serf.State() != SerfState.SerfLeft && DateTime.UtcNow - start < maxWait)
        {
            await Task.Delay(50);
        }

        // Verify transition to Left state
        Assert.Equal(SerfState.SerfLeft, agent.Serf.State());
        
        // Verify local member shows left status
        var localMember = agent.Serf.LocalMember();
        Assert.Equal(MemberStatus.Left, localMember.Status);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_Shutdown_StopsAllProcesses()
    {
        var config = new AgentConfig
        {
            NodeName = "test-shutdown",
            BindAddr = "127.0.0.1:0",
            RpcAddr = "127.0.0.1:0"  // Use dynamic port
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

        // Verify started
        Assert.NotNull(agent.Serf);

        // Shutdown without leave
        await agent.ShutdownAsync();

        // Verify shutdown completed
        Assert.Null(agent.Serf);
        
        // Use reflection to verify _disposed flag
        var disposedField = typeof(SerfAgent).GetField("_disposed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var disposed = (bool)disposedField!.GetValue(agent)!;
        Assert.True(disposed);

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
        
        // Verify not started
        Assert.Null(agent.Serf);

        // Shutdown without starting - should be idempotent
        await agent.ShutdownAsync();

        // Verify still null and no side effects
        Assert.Null(agent.Serf);
        
        // Verify no tasks were created (agent should handle pre-start shutdown gracefully)
        var eventLoopField = typeof(SerfAgent).GetField("_eventLoopTask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var eventLoopTask = eventLoopField!.GetValue(agent);
        Assert.Null(eventLoopTask);

        await agent.DisposeAsync();
    }
}
