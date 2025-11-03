// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce_member_test.go

using NSerf.Serf;
using NSerf.Serf.Coalesce;
using NSerf.Serf.Events;
using System.Net;
using System.Threading.Channels;

namespace NSerfTests.Serf.Coalesce;

/// <summary>
/// Tests for MemberEventCoalescer.
/// Accurately ported from Go's coalesce_member_test.go
/// </summary>
public class MemberEventCoalescerTest
{
    [Fact]
    public async Task MemberEventCoalesce_Basic_ShouldCoalesceCorrectly()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new MemberEventCoalescer();
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
                new MemberEvent
                {
                    Type = EventType.MemberJoin,
                    Members = new List<Member> { new Member { Name = "foo" } }
                },
                new MemberEvent
                {
                    Type = EventType.MemberLeave,
                    Members = new List<Member> { new Member { Name = "foo" } }
                },
                new MemberEvent
                {
                    Type = EventType.MemberLeave,
                    Members = new List<Member> { new Member { Name = "bar" } }
                },
                new MemberEvent
                {
                    Type = EventType.MemberUpdate,
                    Members = new List<Member>
                    {
                        new Member
                        {
                            Name = "zip",
                            Tags = new Dictionary<string, string> { ["role"] = "foo" }
                        }
                    }
                },
                new MemberEvent
                {
                    Type = EventType.MemberUpdate,
                    Members = new List<Member>
                    {
                        new Member
                        {
                            Name = "zip",
                            Tags = new Dictionary<string, string> { ["role"] = "bar" }
                        }
                    }
                },
                new MemberEvent
                {
                    Type = EventType.MemberReap,
                    Members = new List<Member> { new Member { Name = "dead" } }
                }
            };

            // Act - Send all events
            foreach (var e in send)
            {
                await inCh.WriteAsync(e);
            }

            // Collect events with timeout
            var events = new Dictionary<EventType, IEvent>();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

            while (await outChannel.Reader.WaitToReadAsync(timeoutCts.Token))
            {
                if (outChannel.Reader.TryRead(out var evt))
                {
                    events[evt.EventType()] = evt;
                }

                if (events.Count >= 3) break;
            }

            // Assert - Should have 3 event types
            events.Should().HaveCount(3, "should have Leave, Update, and Reap events");

            // Check Leave event
            events.Should().ContainKey(EventType.MemberLeave);
            var leaveEvent = (MemberEvent)events[EventType.MemberLeave];
            leaveEvent.Members.Should().HaveCount(2, "should have 2 leave members");

            var leaveNames = leaveEvent.Members.Select(m => m.Name).OrderBy(n => n).ToList();
            leaveNames.Should().Equal(new[] { "bar", "foo" }, "should have bar and foo");

            // Check Update event
            events.Should().ContainKey(EventType.MemberUpdate);
            var updateEvent = (MemberEvent)events[EventType.MemberUpdate];
            updateEvent.Members.Should().HaveCount(1);
            updateEvent.Members[0].Name.Should().Be("zip");
            updateEvent.Members[0].Tags["role"].Should().Be("bar", "should have latest update");

            // Check Reap event
            events.Should().ContainKey(EventType.MemberReap);
            var reapEvent = (MemberEvent)events[EventType.MemberReap];
            reapEvent.Members.Should().HaveCount(1);
            reapEvent.Members[0].Name.Should().Be("dead");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task MemberEventCoalesce_TagUpdate_ShouldNotSuppress()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new MemberEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5),
            coalescer);

        try
        {
            // Act - Send first update
            await inCh.WriteAsync(new MemberEvent
            {
                Type = EventType.MemberUpdate,
                Members = new List<Member>
                {
                    new Member
                    {
                        Name = "foo",
                        Tags = new Dictionary<string, string> { ["role"] = "foo" }
                    }
                }
            });

            // Wait for first update
            await Task.Delay(TimeSpan.FromMilliseconds(30));

            using var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
            var e1 = await outChannel.Reader.ReadAsync(cts1.Token);
            e1.EventType().Should().Be(EventType.MemberUpdate, "expected first update");

            // Act - Send second update (should not be suppressed even though last event was update)
            await inCh.WriteAsync(new MemberEvent
            {
                Type = EventType.MemberUpdate,
                Members = new List<Member>
                {
                    new Member
                    {
                        Name = "foo",
                        Tags = new Dictionary<string, string> { ["role"] = "bar" }
                    }
                }
            });

            await Task.Delay(TimeSpan.FromMilliseconds(10));

            // Assert - Should receive second update
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
            var e2 = await outChannel.Reader.ReadAsync(cts2.Token);
            e2.EventType().Should().Be(EventType.MemberUpdate, "expected second update");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public void MemberEventCoalesce_Handle_ShouldOnlyHandleMemberEvents()
    {
        // Arrange
        var testCases = new[]
        {
            (Event: (IEvent)new UserEvent(), ShouldHandle: false),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberJoin }, ShouldHandle: true),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberLeave }, ShouldHandle: true),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberFailed }, ShouldHandle: true),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberUpdate }, ShouldHandle: true),
            (Event: (IEvent)new MemberEvent { Type = EventType.MemberReap }, ShouldHandle: true)
        };

        var coalescer = new MemberEventCoalescer();

        // Act & Assert
        foreach (var (evt, shouldHandle) in testCases)
        {
            coalescer.Handle(evt).Should().Be(shouldHandle,
                $"Event {evt.EventType()} should {(shouldHandle ? "" : "not ")}be handled");
        }
    }

    [Fact]
    public async Task MemberEventCoalesce_SameEventSuppression_ShouldNotDuplicateNonUpdates()
    {
        // Arrange
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();

        var coalescer = new MemberEventCoalescer();
        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5),
            coalescer);

        try
        {
            // Act - Send join event
            await inCh.WriteAsync(new MemberEvent
            {
                Type = EventType.MemberJoin,
                Members = new List<Member> { new Member { Name = "test-node" } }
            });

            await Task.Delay(TimeSpan.FromMilliseconds(10));

            // Collect first event
            using var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
            var e1 = await outChannel.Reader.ReadAsync(cts1.Token);
            e1.EventType().Should().Be(EventType.MemberJoin);

            // Act - Send same join event again (should be suppressed)
            await inCh.WriteAsync(new MemberEvent
            {
                Type = EventType.MemberJoin,
                Members = new List<Member> { new Member { Name = "test-node" } }
            });

            await Task.Delay(TimeSpan.FromMilliseconds(10));

            // Assert - Should NOT receive second join (suppressed)
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
            Func<Task> act = async () => await outChannel.Reader.ReadAsync(cts2.Token);
            await act.Should().ThrowAsync<OperationCanceledException>("duplicate join should be suppressed");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public void MemberEventCoalesce_EmptyMembers_ShouldNotCrash()
    {
        // Arrange
        var coalescer = new MemberEventCoalescer();

        // Act
        Action act = () => coalescer.Coalesce(new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        });

        // Assert
        act.Should().NotThrow("empty members list should be handled gracefully");
    }
}
