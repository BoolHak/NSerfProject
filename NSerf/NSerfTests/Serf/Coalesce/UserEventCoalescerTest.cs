// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce_user_test.go

using NSerf.Serf;
using NSerf.Serf.Coalesce;
using NSerf.Serf.Events;
using System.Threading.Channels;

namespace NSerfTests.Serf.Coalesce;

/// <summary>
/// Tests for UserEventCoalescer.
/// Accurately ported from Go's coalesce_user_test.go
/// </summary>
public class UserEventCoalescerTest
{
    [Fact]
    public async Task UserEventCoalesce_Basic_ShouldCoalesceByNameAndLTime()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new UserEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5),
            coalescer);

        try
        {
            var send = new IEvent[]
            {
                new UserEvent
                {
                    LTime = 1,
                    Name = "foo",
                    Coalesce = true
                },
                new UserEvent
                {
                    LTime = 2,
                    Name = "foo",
                    Coalesce = true
                },
                new UserEvent
                {
                    LTime = 2,
                    Name = "bar",
                    Payload = "test1"u8.ToArray(),
                    Coalesce = true
                },
                new UserEvent
                {
                    LTime = 2,
                    Name = "bar",
                    Payload = "test2"u8.ToArray(),
                    Coalesce = true
                }
            };

            // Act - Send all events
            foreach (var e in send)
            {
                await inCh.WriteAsync(e);
            }

            // Collect events with timeout
            var receivedEvents = new List<UserEvent>();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            while (receivedEvents.Count < 3 && await outChannel.Reader.WaitToReadAsync(timeoutCts.Token))
            {
                if (outChannel.Reader.TryRead(out var evt))
                {
                    receivedEvents.Add((UserEvent)evt);
                }
            }

            // Assert
            var gotFoo = false;
            var gotBar1 = false;
            var gotBar2 = false;

            foreach (var ue in receivedEvents)
            {
                if (ue.Name == "foo")
                {
                    ue.LTime.Should().Be((LamportTime)2, "foo should have latest LTime");
                    gotFoo = true;
                }
                else if (ue.Name == "bar")
                {
                    ue.LTime.Should().Be((LamportTime)2, "bar should have LTime 2");

                    if (ue.Payload.SequenceEqual("test1"u8.ToArray()))
                    {
                        gotBar1 = true;
                    }
                    if (ue.Payload.SequenceEqual("test2"u8.ToArray()))
                    {
                        gotBar2 = true;
                    }
                }
            }

            gotFoo.Should().BeTrue("should receive foo event");
            gotBar1.Should().BeTrue("should receive bar with test1");
            gotBar2.Should().BeTrue("should receive bar with test2");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public void UserEventCoalesce_Handle_ShouldOnlyHandleCoalescingUserEvents()
    {
        // Arrange
        var testCases = new[]
        {
            (Event: (IEvent)new UserEvent { Coalesce = false }, ShouldHandle: false),
            (Event: (IEvent)new UserEvent { Coalesce = true }, ShouldHandle: true),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberJoin }, ShouldHandle: false),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberLeave }, ShouldHandle: false),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberFailed }, ShouldHandle: false)
        };

        var coalescer = new UserEventCoalescer();

        // Act & Assert
        foreach (var (evt, shouldHandle) in testCases)
        {
            coalescer.Handle(evt).Should().Be(shouldHandle,
                $"Event {evt.EventType()} (Coalesce={(evt is UserEvent ue ? ue.Coalesce : false)}) should {(shouldHandle ? "" : "not ")}be handled");
        }
    }

    [Fact]
    public async Task UserEventCoalesce_OlderLTime_ShouldBeDiscarded()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new UserEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5),
            coalescer);

        try
        {
            // Act - Send newer event first, then older event
            await inCh.WriteAsync(new UserEvent
            {
                LTime = 10,
                Name = "test",
                Payload = "newer"u8.ToArray(),
                Coalesce = true
            });

            await inCh.WriteAsync(new UserEvent
            {
                LTime = 5,
                Name = "test",
                Payload = "older"u8.ToArray(),
                Coalesce = true
            });

            // Wait for flush
            await Task.Delay(TimeSpan.FromMilliseconds(10));

            // Assert - Should only receive the newer event
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            var evt = await outChannel.Reader.ReadAsync(cts.Token);

            var userEvent = (UserEvent)evt;
            userEvent.LTime.Should().Be((LamportTime)10, "should have newer LTime");
            userEvent.Payload.Should().Equal("newer"u8.ToArray(), "should have newer payload");

            // No more events should be available
            outChannel.Reader.TryRead(out _).Should().BeFalse("older event should be discarded");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task UserEventCoalesce_SameLTime_ShouldKeepBoth()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new UserEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5),
            coalescer);

        try
        {
            // Act - Send two events with same LTime
            await inCh.WriteAsync(new UserEvent
            {
                LTime = 5,
                Name = "test",
                Payload = "first"u8.ToArray(),
                Coalesce = true
            });

            await inCh.WriteAsync(new UserEvent
            {
                LTime = 5,
                Name = "test",
                Payload = "second"u8.ToArray(),
                Coalesce = true
            });

            // Wait for flush
            await Task.Delay(TimeSpan.FromMilliseconds(10));

            // Assert - Should receive both events
            var receivedEvents = new List<UserEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

            while (receivedEvents.Count < 2 && await outChannel.Reader.WaitToReadAsync(cts.Token))
            {
                if (outChannel.Reader.TryRead(out var evt))
                {
                    receivedEvents.Add((UserEvent)evt);
                }
            }

            receivedEvents.Should().HaveCount(2, "both events at same LTime should be kept");
            receivedEvents.All(e => e.LTime == (LamportTime)5).Should().BeTrue();

            var payloads = receivedEvents.Select(e => System.Text.Encoding.UTF8.GetString(e.Payload)).OrderBy(p => p).ToList();
            payloads.Should().Equal(new[] { "first", "second" }, "should receive both payloads");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task UserEventCoalesce_NonCoalescingEvents_ShouldPassThrough()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new UserEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromSeconds(1),  // Long period
            TimeSpan.FromSeconds(1),
            coalescer);

        try
        {
            // Act - Send non-coalescing event
            await inCh.WriteAsync(new UserEvent
            {
                LTime = 1,
                Name = "immediate",
                Payload = "test"u8.ToArray(),
                Coalesce = false  // Not coalescing
            });

            // Assert - Should receive immediately
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var evt = await outChannel.Reader.ReadAsync(cts.Token);

            var userEvent = (UserEvent)evt;
            userEvent.Name.Should().Be("immediate");
            userEvent.Coalesce.Should().BeFalse();
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task UserEventCoalesce_DifferentNames_ShouldBeSeparate()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new UserEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5),
            coalescer);

        try
        {
            // Act - Send events with different names
            await inCh.WriteAsync(new UserEvent
            {
                LTime = 1,
                Name = "event-a",
                Coalesce = true
            });

            await inCh.WriteAsync(new UserEvent
            {
                LTime = 1,
                Name = "event-b",
                Coalesce = true
            });

            await inCh.WriteAsync(new UserEvent
            {
                LTime = 1,
                Name = "event-c",
                Coalesce = true
            });

            // Wait for flush
            await Task.Delay(TimeSpan.FromMilliseconds(10));

            // Assert - Should receive all three events
            var receivedEvents = new List<UserEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

            while (receivedEvents.Count < 3 && await outChannel.Reader.WaitToReadAsync(cts.Token))
            {
                if (outChannel.Reader.TryRead(out var evt))
                {
                    receivedEvents.Add((UserEvent)evt);
                }
            }

            receivedEvents.Should().HaveCount(3, "all three different events should be received");

            var names = receivedEvents.Select(e => e.Name).OrderBy(n => n).ToList();
            names.Should().Equal(new[] { "event-a", "event-b", "event-c" });
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }
}
