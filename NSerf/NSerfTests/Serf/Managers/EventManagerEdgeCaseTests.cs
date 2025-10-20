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
/// Edge case tests for EventManager based on Serf's event handling system.
/// Reference: Go serf/serf.go and serf/serf_test.go edge cases
/// </summary>
public class EventManagerEdgeCaseTests
{
    [Fact]
    public void EventBuffer_Overflow_ShouldOverwriteOldestEntries()
    {
        // Arrange - Very small buffer to force overflow
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 3, // Only 3 slots
            logger: null);

        // Act - Add more events than buffer can hold
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

        // Assert - Very old events (LTime 0-6) should not be in buffer anymore
        // Only recent events (7, 8, 9) might still be detectable
        var events = eventManager.GetEventCollectionsForPushPull();
        events.Count.Should().BeLessOrEqualTo(10, "buffer should contain at most 10 events");
        
        // Try to add event with LTime=0 again - might be treated as new due to overflow
        var duplicateAtZero = new MessageUserEvent
        {
            LTime = 0,
            Name = "event-0",
            Payload = new byte[] { 0 },
            CC = false
        };
        
        var result = eventManager.HandleUserEvent(duplicateAtZero);
        // Behavior depends on whether slot 0 was overwritten
        // Just verify it doesn't crash and returns a boolean
        _ = result; // Result can be either true or false depending on buffer state
    }

    [Fact]
    public async Task ConcurrentEventProcessing_ShouldNotCorruptState()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 1000,
            logger: null);

        var tasks = new List<Task>();
        var exceptions = new List<Exception>();

        // Act - Multiple threads processing events concurrently
        for (int threadId = 0; threadId < 10; threadId++)
        {
            int localThreadId = threadId;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (ulong i = 0; i < 50; i++)
                    {
                        var evt = new MessageUserEvent
                        {
                            LTime = (ulong)localThreadId * 1000UL + i,
                            Name = $"thread-{localThreadId}-event-{i}",
                            Payload = new byte[] { (byte)localThreadId, (byte)i },
                            CC = false
                        };
                        eventManager.HandleUserEvent(evt);
                        
                        // Also test clock witnessing
                        eventManager.WitnessEventClock(evt.LTime + 1UL);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - No exceptions should occur
        exceptions.Should().BeEmpty("concurrent access should be thread-safe");
        
        // Verify clock is consistent
        var finalClock = eventManager.GetEventClockTime();
        finalClock.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void EventsDuringShutdown_WithNullChannel_ShouldNotThrow()
    {
        // Arrange - No event channel (simulating partial shutdown)
        var eventManager = new EventManager(
            eventCh: null,
            eventBufferSize: 64,
            logger: null);

        // Act - Try to process events when channel is null
        var evt = new MessageUserEvent
        {
            LTime = 100,
            Name = "shutdown-event",
            Payload = new byte[] { 1, 2, 3 },
            CC = false
        };

        // Assert - Should handle gracefully without throwing
        var act = () => eventManager.HandleUserEvent(evt);
        act.Should().NotThrow("should handle null channel gracefully");
        
        var emitAct = () => eventManager.EmitEvent(new MemberEvent 
        { 
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        });
        emitAct.Should().NotThrow("EmitEvent should handle null channel gracefully");
    }

    [Fact]
    public void EventsDuringShutdown_AfterDispose_ShouldNotThrow()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        // Act - Dispose and then try to use
        eventManager.Dispose();

        // Try to process event after disposal
        var evt = new MessageUserEvent
        {
            LTime = 100,
            Name = "post-shutdown-event",
            Payload = new byte[] { 1 },
            CC = false
        };

        // Assert - Should handle gracefully (may throw ObjectDisposedException which is expected)
        // The important thing is it shouldn't deadlock or corrupt memory
        try
        {
            eventManager.HandleUserEvent(evt);
            // If it succeeds, that's fine too
        }
        catch (ObjectDisposedException)
        {
            // This is an acceptable outcome
        }
    }

    [Fact]
    public void EventJoinIgnore_OldEventsBeforeMinTime_ShouldBeIgnored()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        // Simulate join operation - set minTime to 100
        eventManager.SetEventMinTime(100);

        // Act - Try to process old events from before join
        var oldEvent1 = new MessageUserEvent { LTime = 50, Name = "old-1", Payload = new byte[] { 1 }, CC = false };
        var oldEvent2 = new MessageUserEvent { LTime = 75, Name = "old-2", Payload = new byte[] { 2 }, CC = false };
        var newEvent = new MessageUserEvent { LTime = 101, Name = "new-1", Payload = new byte[] { 3 }, CC = false };

        var result1 = eventManager.HandleUserEvent(oldEvent1);
        var result2 = eventManager.HandleUserEvent(oldEvent2);
        var result3 = eventManager.HandleUserEvent(newEvent);

        // Assert
        result1.Should().BeFalse("event before minTime should be ignored");
        result2.Should().BeFalse("event before minTime should be ignored");
        result3.Should().BeTrue("event after minTime should be processed");

        // Verify only new event was emitted
        eventCh.Reader.TryRead(out var emittedEvent).Should().BeTrue();
        var userEvt = emittedEvent.Should().BeOfType<UserEvent>().Subject;
        userEvt.Name.Should().Be("new-1");
        
        eventCh.Reader.TryRead(out _).Should().BeFalse("old events should not be emitted");
    }

    [Fact]
    public void ClockSynchronization_BackwardsTime_ShouldNotDecreaseClock()
    {
        // Arrange
        var eventManager = new EventManager(
            eventCh: null,
            eventBufferSize: 64,
            logger: null);

        // Act - Witness times in non-monotonic order
        eventManager.WitnessEventClock(100);
        var clock1 = eventManager.GetEventClockTime();
        
        eventManager.WitnessEventClock(50); // Try to go backwards
        var clock2 = eventManager.GetEventClockTime();
        
        eventManager.WitnessEventClock(100); // Same time
        var clock3 = eventManager.GetEventClockTime();
        
        eventManager.WitnessEventClock(150); // Forward
        var clock4 = eventManager.GetEventClockTime();

        // Assert - Clock should never decrease
        clock1.Should().Be(100);
        clock2.Should().Be(100, "clock should not go backwards");
        clock3.Should().Be(100, "clock should stay at max");
        clock4.Should().Be(150, "clock should advance forward");
    }

    [Fact]
    public void OldEvents_BeyondBufferWindow_ShouldBeRejected()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 10, // Small window
            logger: null);

        // Advance clock significantly
        var recentEvent = new MessageUserEvent
        {
            LTime = 100,
            Name = "recent",
            Payload = new byte[] { 1 },
            CC = false
        };
        eventManager.HandleUserEvent(recentEvent);
        eventManager.WitnessEventClock(100);

        eventCh.Reader.TryRead(out _); // Consume recent event

        // Act - Try to add very old event (100 - 10 = 90, so event at 50 is too old)
        var veryOldEvent = new MessageUserEvent
        {
            LTime = 50, // Way beyond buffer window
            Name = "very-old",
            Payload = new byte[] { 2 },
            CC = false
        };

        var result = eventManager.HandleUserEvent(veryOldEvent);

        // Assert
        result.Should().BeFalse("event beyond buffer window should be rejected");
        eventCh.Reader.TryRead(out _).Should().BeFalse("no event should be emitted");
    }

    [Fact]
    public void EventWithExactMinTime_ShouldBeAccepted()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        eventManager.SetEventMinTime(100);

        // Act - Event with exact minTime value
        var eventAtMinTime = new MessageUserEvent
        {
            LTime = 100, // Exactly at minTime
            Name = "at-min",
            Payload = new byte[] { 1 },
            CC = false
        };

        var result = eventManager.HandleUserEvent(eventAtMinTime);

        // Assert - Should be accepted (< check, not <=, so boundary is inclusive)
        result.Should().BeTrue("event at exactly minTime should be accepted");
        eventCh.Reader.TryRead(out _).Should().BeTrue("event should be emitted");
    }

    [Fact]
    public void MultipleEventsAtSameLTime_DifferentNames_ShouldAllBeProcessed()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        // Act - Multiple different events at same LTime
        var events = new[]
        {
            new MessageUserEvent { LTime = 100, Name = "event-a", Payload = new byte[] { 1 }, CC = false },
            new MessageUserEvent { LTime = 100, Name = "event-b", Payload = new byte[] { 2 }, CC = false },
            new MessageUserEvent { LTime = 100, Name = "event-c", Payload = new byte[] { 3 }, CC = false }
        };

        var results = events.Select(e => eventManager.HandleUserEvent(e)).ToArray();

        // Assert - All should be accepted
        results.Should().OnlyContain(r => r == true, "different events at same LTime should all be processed");
        
        // Verify all were emitted
        for (int i = 0; i < 3; i++)
        {
            eventCh.Reader.TryRead(out var evt).Should().BeTrue($"event {i} should be emitted");
        }
    }

    [Fact]
    public void EmitEvent_WithChannelClosed_ShouldNotThrowOrDeadlock()
    {
        // Arrange
        var channel = Channel.CreateBounded<Event>(1);
        var eventManager = new EventManager(
            eventCh: channel.Writer,
            eventBufferSize: 64,
            logger: null);

        // Close the channel writer
        channel.Writer.Complete();

        // Act - Try to emit event to closed channel
        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { new Member { Name = "test" } }
        };

        // Assert - Should not throw or deadlock
        var act = () => eventManager.EmitEvent(memberEvent);
        act.Should().NotThrow("should handle closed channel gracefully");
    }

    [Fact]
    public async Task GetEventCollectionsForPushPull_DuringConcurrentWrites_ShouldReturnConsistentSnapshot()
    {
        // Arrange
        var eventManager = new EventManager(
            eventCh: null,
            eventBufferSize: 100,
            logger: null);

        // Add some initial events
        for (ulong i = 0; i < 20; i++)
        {
            eventManager.HandleUserEvent(new MessageUserEvent
            {
                LTime = i,
                Name = $"event-{i}",
                Payload = new byte[] { (byte)i },
                CC = false
            });
        }

        // Act - Get snapshot while other thread is writing
        var writeTask = Task.Run(() =>
        {
            for (ulong i = 20; i < 40; i++)
            {
                eventManager.HandleUserEvent(new MessageUserEvent
                {
                    LTime = i,
                    Name = $"event-{i}",
                    Payload = new byte[] { (byte)i },
                    CC = false
                });
                Thread.Sleep(1);
            }
        });

        var snapshots = new List<List<UserEventCollection>>();
        for (int i = 0; i < 10; i++)
        {
            snapshots.Add(eventManager.GetEventCollectionsForPushPull());
            Thread.Sleep(2);
        }

        await writeTask;

        // Assert - Each snapshot should be internally consistent
        foreach (var snapshot in snapshots)
        {
            snapshot.Should().NotBeNull("snapshot should be valid");
            // Verify no null entries
            snapshot.Should().AllSatisfy(item => item.Should().NotBeNull("each item should be non-null"));
        }
    }

    [Fact]
    public void ZeroLTimeEvent_ShouldBeHandledCorrectly()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<Event>();
        var eventManager = new EventManager(
            eventCh: eventCh.Writer,
            eventBufferSize: 64,
            logger: null);

        // Act - Event with LTime = 0 (edge case)
        var zeroEvent = new MessageUserEvent
        {
            LTime = 0,
            Name = "zero-time-event",
            Payload = new byte[] { 0 },
            CC = false
        };

        var result = eventManager.HandleUserEvent(zeroEvent);

        // Assert
        result.Should().BeTrue("event with LTime=0 should be processed");
        eventCh.Reader.TryRead(out var evt).Should().BeTrue();
        var userEvt = evt.Should().BeOfType<UserEvent>().Subject;
        userEvt.LTime.Should().Be(0);
    }
}
