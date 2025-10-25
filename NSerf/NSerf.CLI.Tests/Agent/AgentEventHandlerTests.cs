// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using NSerf.CLI.Tests.Fixtures;
using NSerf.Serf.Events;

namespace NSerf.CLI.Tests.Agent;

/// <summary>
/// Tests for SerfAgent event handler system ported from Go's agent/agent_test.go
/// Validates event handler registration, execution, and filtering.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class AgentEventHandlerTests : IAsyncLifetime
{
    private AgentFixture? _fixture;
    private MockEventHandler? _handler;

    public async Task InitializeAsync()
    {
        _fixture = new AgentFixture();
        await _fixture.InitializeAsync();
        
        _handler = new MockEventHandler();
        _fixture.Agent!.RegisterEventHandler(_handler);
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    /// <summary>
    /// Test: TestAgentUserEventHandler
    /// Validates that registered event handlers receive user events
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Agent_UserEventHandler_ReceivesEvents()
    {
        // Arrange
        var eventName = "deploy";
        var payload = System.Text.Encoding.UTF8.GetBytes("foo");

        // Act - trigger user event
        await _fixture!.Agent!.Serf!.UserEventAsync(eventName, payload, coalesce: false);
        
        // Wait for event to propagate
        await Task.Delay(500);

        // Assert
        Assert.NotEmpty(_handler!.ReceivedEvents);
        var userEvent = _handler.ReceivedEvents.OfType<UserEvent>().FirstOrDefault();
        Assert.NotNull(userEvent);
        Assert.Equal(eventName, userEvent.Name);
        Assert.Equal(payload, userEvent.Payload);
    }

    /// <summary>
    /// Test: TestAgentUserEventHandler_Multiple
    /// Validates that multiple handlers can be registered
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Agent_MultipleEventHandlers_AllReceiveEvents()
    {
        // Arrange
        var handler2 = new MockEventHandler();
        _fixture!.Agent!.RegisterEventHandler(handler2);
        
        var eventName = "test-event";
        var payload = System.Text.Encoding.UTF8.GetBytes("test-payload");

        // Act
        await _fixture.Agent.Serf!.UserEventAsync(eventName, payload, coalesce: false);
        await Task.Delay(500);

        // Assert - both handlers received the event
        Assert.NotEmpty(_handler!.ReceivedEvents);
        Assert.NotEmpty(handler2.ReceivedEvents);
        
        var event1 = _handler.ReceivedEvents.OfType<UserEvent>().FirstOrDefault();
        var event2 = handler2.ReceivedEvents.OfType<UserEvent>().FirstOrDefault();
        
        Assert.NotNull(event1);
        Assert.NotNull(event2);
        Assert.Equal(eventName, event1.Name);
        Assert.Equal(eventName, event2.Name);
    }

    /// <summary>
    /// Test: TestAgentEventHandler (member events)
    /// Validates that handlers receive member join/leave events
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task Agent_MemberEventHandler_ReceivesMemberEvents()
    {
        // Arrange - create second agent to join
        await using var agent2 = new AgentFixture();
        await agent2.InitializeAsync();
        
        var agent2Members = agent2.Agent!.Serf!.Members();
        var agent2Addr = $"{agent2Members[0].Addr}:{agent2Members[0].Port}";

        // Act - join the agents
        await _fixture!.Agent!.Serf!.JoinAsync(new[] { agent2Addr }, ignoreOld: false);
        
        // Wait for join event to propagate
        await Task.Delay(2000);

        // Assert - should have received join event
        Assert.NotEmpty(_handler!.ReceivedEvents);
        var joinEvent = _handler.ReceivedEvents.OfType<MemberEvent>()
            .FirstOrDefault(e => e.EventType() == EventType.MemberJoin);
        
        Assert.NotNull(joinEvent);
        Assert.Contains(joinEvent.Members, m => m.Name == agent2.Agent.NodeName);
    }

    /// <summary>
    /// Test: Event handler deregistration
    /// Validates that deregistered handlers no longer receive events
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Agent_DeregisterEventHandler_StopsReceivingEvents()
    {
        // Arrange
        var initialEventCount = _handler!.ReceivedEvents.Count;
        
        // Deregister the handler
        _fixture!.Agent!.DeregisterEventHandler(_handler);
        
        // Act - trigger event
        await _fixture.Agent.Serf!.UserEventAsync("test", new byte[] { 1, 2, 3 }, coalesce: false);
        await Task.Delay(500);

        // Assert - handler should not have received new events
        Assert.Equal(initialEventCount, _handler.ReceivedEvents.Count);
    }

    /// <summary>
    /// Test: Event handler exception isolation
    /// Validates that exceptions in one handler don't affect others
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Agent_EventHandlerException_DoesNotAffectOthers()
    {
        // Arrange
        var faultyHandler = new FaultyEventHandler();
        var goodHandler = new MockEventHandler();
        
        _fixture!.Agent!.RegisterEventHandler(faultyHandler);
        _fixture.Agent.RegisterEventHandler(goodHandler);

        // Act - trigger event (faultyHandler will throw)
        await _fixture.Agent.Serf!.UserEventAsync("test", new byte[] { 1 }, coalesce: false);
        await Task.Delay(500);

        // Assert - goodHandler should still have received the event
        Assert.NotEmpty(goodHandler.ReceivedEvents);
        var userEvent = goodHandler.ReceivedEvents.OfType<UserEvent>().FirstOrDefault();
        Assert.NotNull(userEvent);
    }

    /// <summary>
    /// Test: Query events trigger handlers
    /// Validates that query events are dispatched to handlers
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task Agent_QueryEvent_TriggersHandler()
    {
        // Arrange
        var queryName = "ping";
        var payload = System.Text.Encoding.UTF8.GetBytes("ping-data");

        // Act - trigger query
        await _fixture!.Agent!.Serf!.QueryAsync(queryName, payload, null);
        await Task.Delay(500);

        // Assert - handler should have received query event
        Assert.NotEmpty(_handler!.ReceivedEvents);
        var queryEvent = _handler.ReceivedEvents.OfType<Query>().FirstOrDefault();
        Assert.NotNull(queryEvent);
        Assert.Equal(queryName, queryEvent.Name);
    }
}

/// <summary>
/// Mock event handler for testing
/// </summary>
public class MockEventHandler : IEventHandler
{
    public List<Event> ReceivedEvents { get; } = new();
    
    public void HandleEvent(Event evt)
    {
        ReceivedEvents.Add(evt);
    }
}

/// <summary>
/// Event handler that throws exceptions for testing error isolation
/// </summary>
public class FaultyEventHandler : IEventHandler
{
    public void HandleEvent(Event evt)
    {
        throw new InvalidOperationException("Simulated handler failure");
    }
}
