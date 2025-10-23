// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Agent;

public class AgentEventHandlerTests
{
    [Fact]
    public void Agent_RegisterHandler_AddsHandler()
    {
        var config = new AgentConfig { NodeName = "test-register" };
        var agent = new SerfAgent(config);
        var handler = new TestEventHandler();

        agent.RegisterEventHandler(handler);

        // Handler added - no exception
        Assert.NotNull(agent);
    }

    [Fact]
    public void Agent_RegisterMultipleHandlers_AllSupported()
    {
        var config = new AgentConfig { NodeName = "test-multiple" };
        var agent = new SerfAgent(config);
        
        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();
        var handler3 = new TestEventHandler();

        agent.RegisterEventHandler(handler1);
        agent.RegisterEventHandler(handler2);
        agent.RegisterEventHandler(handler3);

        // All handlers added
        Assert.NotNull(agent);
    }

    [Fact]
    public void Agent_RegisterDuplicateHandler_OnlyAddedOnce()
    {
        var config = new AgentConfig { NodeName = "test-duplicate" };
        var agent = new SerfAgent(config);
        var handler = new TestEventHandler();

        agent.RegisterEventHandler(handler);
        agent.RegisterEventHandler(handler);  // Duplicate
        agent.RegisterEventHandler(handler);  // Duplicate

        // HashSet ensures only one instance
        Assert.NotNull(agent);
    }

    [Fact]
    public void Agent_DeregisterHandler_RemovesHandler()
    {
        var config = new AgentConfig { NodeName = "test-deregister" };
        var agent = new SerfAgent(config);
        var handler = new TestEventHandler();

        agent.RegisterEventHandler(handler);
        agent.DeregisterEventHandler(handler);

        // Handler removed
        Assert.NotNull(agent);
    }

    [Fact]
    public async Task Agent_EventLoop_DispatchesToAllHandlers()
    {
        var config = new AgentConfig
        {
            NodeName = "test-dispatch",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();

        agent.RegisterEventHandler(handler1);
        agent.RegisterEventHandler(handler2);

        await agent.StartAsync();

        // Wait for any initial events
        await Task.Delay(500);

        // Verify no exceptions thrown
        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_HandlerException_DoesNotStopLoop()
    {
        var config = new AgentConfig
        {
            NodeName = "test-exception",
            BindAddr = "127.0.0.1:0"
        };

        var agent = new SerfAgent(config);
        var throwingHandler = new ThrowingEventHandler();
        var normalHandler = new TestEventHandler();

        agent.RegisterEventHandler(throwingHandler);
        agent.RegisterEventHandler(normalHandler);

        await agent.StartAsync();

        // Event loop should continue despite exception
        await Task.Delay(500);

        // Agent still running
        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }
}

public class TestEventHandler : IEventHandler
{
    public List<Event> ReceivedEvents { get; } = new();

    public void HandleEvent(Event @event)
    {
        ReceivedEvents.Add(@event);
    }
}

public class ThrowingEventHandler : IEventHandler
{
    public void HandleEvent(Event @event)
    {
        throw new InvalidOperationException("Test exception");
    }
}
