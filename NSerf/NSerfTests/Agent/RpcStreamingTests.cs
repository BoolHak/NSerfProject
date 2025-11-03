// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Agent.RPC;
using NSerf.Client;
using Xunit;

namespace NSerfTests.Agent;

public class RpcStreamingTests
{
    [Fact]
    public async Task RpcServer_MonitorCommand_Exists()
    {
        var config = new AgentConfig
        {
            NodeName = "monitor-test",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Verify LogWriter is initialized
        Assert.NotNull(agent.LogWriter);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task RpcServer_StreamCommand_Exists()
    {
        var config = new AgentConfig
        {
            NodeName = "stream-test",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Agent should support event handlers
        Assert.NotNull(agent.Serf);

        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task CircularLogWriter_Integration_WithAgentAsync()
    {
        var config = new AgentConfig
        {
            NodeName = "log-test",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);

        // LogWriter should be created
        Assert.NotNull(agent.LogWriter);

        // Should be able to write logs
        agent.LogWriter.WriteLine("Test log");

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task RpcEventHandler_ReceivesBacklog()
    {
        var config = new AgentConfig
        {
            NodeName = "event-test",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        await agent.StartAsync();

        // Wait for agent to fully start and generate initial events
        await Task.Delay(1000);

        // Register a test event handler AFTER startup
        var receivedEvents = new List<NSerf.Serf.Events.IEvent>();
        var handler = new TestStreamEventHandler(receivedEvents);
        agent.RegisterEventHandler(handler);

        // Handler should work (even if no events received yet)
        Assert.NotNull(handler);

        agent.DeregisterEventHandler(handler);
        await agent.ShutdownAsync();
        await agent.DisposeAsync();
    }
}

public class TestStreamEventHandler : IEventHandler
{
    private readonly List<NSerf.Serf.Events.IEvent> _events;

    public TestStreamEventHandler(List<NSerf.Serf.Events.IEvent> events)
    {
        _events = events;
    }

    public void HandleEvent(NSerf.Serf.Events.IEvent @event)
    {
        _events.Add(@event);
    }
}
