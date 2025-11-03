// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.Serf.Events;
using System.Reflection;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Agent;

public class AgentEventHandlerTests
{
    [Fact]
    public async Task Agent_RegisterHandler_AddsHandler()
    {
        var config = new AgentConfig
        {
            NodeName = "test-register",
            BindAddr = "127.0.0.1:0"
        };
        var agent = new SerfAgent(config);
        var handler = new TestEventHandler();

        agent.RegisterEventHandler(handler);
        await agent.StartAsync();

        // Wait for initial MemberJoin event (local node) to be processed
        await Task.Delay(100);

        // Clear initial event
        handler.ReceivedEvents.Clear();

        // Access internal event channel via reflection to inject test event
        var eventChannelField = typeof(SerfAgent).GetField("_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventChannel = (Channel<IEvent>)eventChannelField!.GetValue(agent)!;

        // Write test event
        var testEvent = new MemberEvent { Type = EventType.MemberJoin };
        await eventChannel.Writer.WriteAsync(testEvent);

        // Wait for event loop to process
        await Task.Delay(100);

        // Verify handler received the test event
        Assert.Single(handler.ReceivedEvents);
        Assert.IsType<MemberEvent>(handler.ReceivedEvents[0]);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_RegisterMultipleHandlers_AllSupported()
    {
        var config = new AgentConfig
        {
            NodeName = "test-multiple",
            BindAddr = "127.0.0.1:0"
        };
        var agent = new SerfAgent(config);

        var handler1 = new TestEventHandler();
        var handler2 = new TestEventHandler();
        var handler3 = new TestEventHandler();

        agent.RegisterEventHandler(handler1);
        agent.RegisterEventHandler(handler2);
        agent.RegisterEventHandler(handler3);

        await agent.StartAsync();

        // Wait for initial MemberJoin event (local node) to be processed
        await Task.Delay(100);

        // Clear initial events
        handler1.ReceivedEvents.Clear();
        handler2.ReceivedEvents.Clear();
        handler3.ReceivedEvents.Clear();

        // Inject test event
        var eventChannelField = typeof(SerfAgent).GetField("_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventChannel = (Channel<IEvent>)eventChannelField!.GetValue(agent)!;
        var testEvent = new MemberEvent { Type = EventType.MemberJoin };
        await eventChannel.Writer.WriteAsync(testEvent);

        await Task.Delay(100);

        // All handlers should receive the test event
        Assert.Single(handler1.ReceivedEvents);
        Assert.Single(handler2.ReceivedEvents);
        Assert.Single(handler3.ReceivedEvents);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_RegisterDuplicateHandler_OnlyAddedOnce()
    {
        var config = new AgentConfig
        {
            NodeName = "test-duplicate",
            BindAddr = "127.0.0.1:0"
        };
        var agent = new SerfAgent(config);
        var handler = new TestEventHandler();

        agent.RegisterEventHandler(handler);
        agent.RegisterEventHandler(handler);  // Duplicate
        agent.RegisterEventHandler(handler);  // Duplicate

        await agent.StartAsync();

        // Wait for initial MemberJoin event (local node) to be processed
        await Task.Delay(100);

        // Clear initial event
        handler.ReceivedEvents.Clear();

        // Inject test event
        var eventChannelField = typeof(SerfAgent).GetField("_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventChannel = (Channel<IEvent>)eventChannelField!.GetValue(agent)!;
        var testEvent = new MemberEvent { Type = EventType.MemberJoin };
        await eventChannel.Writer.WriteAsync(testEvent);

        await Task.Delay(100);

        // Handler should receive test event only once (HashSet deduplication)
        Assert.Single(handler.ReceivedEvents);

        await agent.DisposeAsync();
    }

    [Fact]
    public async Task Agent_DeregisterHandler_RemovesHandler()
    {
        var config = new AgentConfig
        {
            NodeName = "test-deregister",
            BindAddr = "127.0.0.1:0"
        };
        var agent = new SerfAgent(config);
        var handler = new TestEventHandler();

        agent.RegisterEventHandler(handler);
        await agent.StartAsync();

        // Wait for initial MemberJoin event (local node) to be processed
        await Task.Delay(100);

        // Deregister handler and clear any received events
        agent.DeregisterEventHandler(handler);
        handler.ReceivedEvents.Clear();

        // Inject test event after deregistration
        var eventChannelField = typeof(SerfAgent).GetField("_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventChannel = (Channel<IEvent>)eventChannelField!.GetValue(agent)!;
        var testEvent = new MemberEvent { Type = EventType.MemberJoin };
        await eventChannel.Writer.WriteAsync(testEvent);

        await Task.Delay(100);

        // Handler should NOT receive the test event (was deregistered)
        Assert.Empty(handler.ReceivedEvents);

        await agent.DisposeAsync();
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

        // Wait for initial MemberJoin event (local node) to be processed
        await Task.Delay(100);

        // Clear initial events (local node join)
        handler1.ReceivedEvents.Clear();
        handler2.ReceivedEvents.Clear();

        // Inject multiple test events
        var eventChannelField = typeof(SerfAgent).GetField("_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventChannel = (Channel<IEvent>)eventChannelField!.GetValue(agent)!;

        await eventChannel.Writer.WriteAsync(new MemberEvent { Type = EventType.MemberJoin });
        await eventChannel.Writer.WriteAsync(new MemberEvent { Type = EventType.MemberLeave });

        await Task.Delay(200);

        // Both handlers should receive both test events
        Assert.Equal(2, handler1.ReceivedEvents.Count);
        Assert.Equal(2, handler2.ReceivedEvents.Count);

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

        // Wait for initial MemberJoin event (local node) to be processed
        await Task.Delay(100);

        // Clear initial events
        normalHandler.ReceivedEvents.Clear();

        // Inject events - first will cause exception, second should still be processed
        var eventChannelField = typeof(SerfAgent).GetField("_eventChannel", BindingFlags.NonPublic | BindingFlags.Instance);
        var eventChannel = (Channel<IEvent>)eventChannelField!.GetValue(agent)!;

        await eventChannel.Writer.WriteAsync(new MemberEvent { Type = EventType.MemberJoin });
        await Task.Delay(100);
        await eventChannel.Writer.WriteAsync(new MemberEvent { Type = EventType.MemberLeave });
        await Task.Delay(100);

        // Normal handler should receive both test events despite throwing handler
        Assert.Equal(2, normalHandler.ReceivedEvents.Count);
        Assert.NotNull(agent.Serf);

        await agent.DisposeAsync();
    }
}

public class TestEventHandler : IEventHandler
{
    public List<IEvent> ReceivedEvents { get; } = new();

    public void HandleEvent(IEvent @event)
    {
        ReceivedEvents.Add(@event);
    }
}

public class ThrowingEventHandler : IEventHandler
{
    public void HandleEvent(IEvent @event)
    {
        throw new InvalidOperationException("Test exception");
    }
}
