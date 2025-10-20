// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Events;
using NSerf.Serf.Managers;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Serf.Managers;

/// <summary>
/// Tests for EventManager - handles user event buffering, deduplication, and emission.
/// Reference: Go serf/serf.go handleUserEvent() function
/// </summary>
public class EventManagerTests
{
    [Fact]
    public void HandleUserEvent_WithNewEvent_ShouldAddToBufferAndEmit()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        var userEvent = new MessageUserEvent
        {
            LTime = 100,
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 },
            CC = false
        };

        // Act
        var shouldRebroadcast = eventManager.HandleUserEvent(userEvent);

        // Assert
        shouldRebroadcast.Should().BeTrue("new events should be rebroadcast");
        
        // Verify event was emitted
        eventCh.Reader.TryRead(out var emittedEvent).Should().BeTrue();
        var evt = emittedEvent.Should().BeOfType<UserEvent>().Subject;
        evt.LTime.Should().Be(100);
        evt.Name.Should().Be("test-event");
        evt.Payload.Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void HandleUserEvent_WithDuplicateEvent_ShouldNotEmitOrRebroadcast()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        var userEvent = new MessageUserEvent
        {
            LTime = 100,
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 },
            CC = false
        };

        // First event - should succeed
        eventManager.HandleUserEvent(userEvent);
        eventCh.Reader.TryRead(out _); // Consume first event

        // Act - send duplicate
        var shouldRebroadcast = eventManager.HandleUserEvent(userEvent);

        // Assert
        shouldRebroadcast.Should().BeFalse("duplicate events should not be rebroadcast");
        eventCh.Reader.TryRead(out _).Should().BeFalse("no new event should be emitted");
    }

    [Fact]
    public void HandleUserEvent_WithEventBelowMinTime_ShouldIgnore()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        // Set minimum time to 100
        eventManager.SetEventMinTime(100);

        var oldEvent = new MessageUserEvent
        {
            LTime = 50, // Below minTime
            Name = "old-event",
            Payload = new byte[] { 1 },
            CC = false
        };

        // Act
        var shouldRebroadcast = eventManager.HandleUserEvent(oldEvent);

        // Assert
        shouldRebroadcast.Should().BeFalse("events below minTime should not be processed");
        eventCh.Reader.TryRead(out _).Should().BeFalse("no event should be emitted");
    }

    [Fact]
    public void HandleUserEvent_WithTooOldEvent_ShouldIgnore()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 10, // Small buffer
            logger: null);

        // Add a recent event to advance the clock
        var recentEvent = new MessageUserEvent
        {
            LTime = 100,
            Name = "recent",
            Payload = new byte[] { 1 },
            CC = false
        };
        eventManager.HandleUserEvent(recentEvent);
        eventManager.WitnessEventClock(100);

        // Try to add a very old event (curTime=100, bufferSize=10, so event must be >= 90)
        var oldEvent = new MessageUserEvent
        {
            LTime = 50, // Too old (100 - 10 = 90, but this is 50)
            Name = "old",
            Payload = new byte[] { 2 },
            CC = false
        };

        eventCh.Reader.TryRead(out _); // Consume recent event

        // Act
        var shouldRebroadcast = eventManager.HandleUserEvent(oldEvent);

        // Assert
        shouldRebroadcast.Should().BeFalse("events outside buffer window should be ignored");
        eventCh.Reader.TryRead(out _).Should().BeFalse("no event should be emitted");
    }

    [Fact]
    public void HandleUserEvent_WithSameLTimeDifferentPayload_ShouldEmitBoth()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        var event1 = new MessageUserEvent
        {
            LTime = 100,
            Name = "event-a",
            Payload = new byte[] { 1 },
            CC = false
        };

        var event2 = new MessageUserEvent
        {
            LTime = 100, // Same LTime
            Name = "event-b", // Different name
            Payload = new byte[] { 2 },
            CC = false
        };

        // Act
        var result1 = eventManager.HandleUserEvent(event1);
        var result2 = eventManager.HandleUserEvent(event2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
        
        // Both events should be emitted
        eventCh.Reader.TryRead(out var evt1).Should().BeTrue();
        eventCh.Reader.TryRead(out var evt2).Should().BeTrue();
    }

    [Fact]
    public void HandleUserEvent_WithNoEventChannel_ShouldStillBuffer()
    {
        // Arrange - no event channel
        var eventManager = new EventManager(
            eventCh: null,
            eventBufferSize: 64,
            logger: null);

        var userEvent = new MessageUserEvent
        {
            LTime = 100,
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 },
            CC = false
        };

        // Act
        var shouldRebroadcast = eventManager.HandleUserEvent(userEvent);

        // Assert
        shouldRebroadcast.Should().BeTrue("event should still be processed and rebroadcast");
        
        // Try duplicate - should be detected even without EventCh
        var shouldRebroadcastDupe = eventManager.HandleUserEvent(userEvent);
        shouldRebroadcastDupe.Should().BeFalse("duplicate should still be detected");
    }

    [Fact]
    public void WitnessEventClock_ShouldUpdateClock()
    {
        // Arrange
        var eventManager = new EventManager(
            eventCh: null,
            eventBufferSize: 64,
            logger: null);

        // Act
        eventManager.WitnessEventClock(50);
        eventManager.WitnessEventClock(100);
        eventManager.WitnessEventClock(75); // Should not decrease

        // Assert - clock should be at 100
        var currentTime = eventManager.GetEventClockTime();
        currentTime.Should().Be(100);
    }

    [Fact]
    public void CircularBuffer_ShouldWrapAround()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 4, // Very small buffer
            logger: null);

        // Act - Add events that will wrap around the buffer
        for (ulong i = 0; i < 10; i++)
        {
            var evt = new MessageUserEvent
            {
                LTime = i,
                Name = $"event-{i}",
                Payload = new byte[] { (byte)i },
                CC = false
            };
            eventManager.HandleUserEvent(evt);
        }

        // Try to add event at LTime=0 again (should be treated as duplicate due to buffer wrap)
        var duplicateAtZero = new MessageUserEvent
        {
            LTime = 0,
            Name = "event-0",
            Payload = new byte[] { 0 },
            CC = false
        };

        var result = eventManager.HandleUserEvent(duplicateAtZero);

        // Assert
        result.Should().BeFalse("event at index 0 should be overwritten but still detected");
    }

    [Fact]
    public void EmitEvent_WithMemberJoinEvent_ShouldEmit()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { new Member { Name = "test-node" } }
        };

        // Act
        eventManager.EmitEvent(memberEvent);

        // Assert
        eventCh.Reader.TryRead(out var emittedEvent).Should().BeTrue();
        var evt = emittedEvent.Should().BeOfType<MemberEvent>().Subject;
        evt.Type.Should().Be(EventType.MemberJoin);
    }

    [Fact]
    public void EmitEvent_WithNoChannel_ShouldNotThrow()
    {
        // Arrange
        var eventManager = new EventManager(
            eventCh: null,
            eventBufferSize: 64,
            logger: null);

        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { new Member { Name = "test-node" } }
        };

        // Act & Assert - should not throw
        var act = () => eventManager.EmitEvent(memberEvent);
        act.Should().NotThrow();
    }
}
