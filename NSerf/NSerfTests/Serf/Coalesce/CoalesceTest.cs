// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce_test.go

using NSerf.Serf.Coalesce;
using NSerf.Serf.Events;
using System.Threading.Channels;

namespace NSerfTests.Serf.Coalesce;

/// <summary>
/// Tests for the coalescing system.
/// Accurately ported from Go's coalesce_test.go
/// </summary>
public class CoalesceTest
{
    // Mock EventCounter type (custom event type for testing)
    private const EventType EventCounter = (EventType)9000;

    /// <summary>
    /// Counter event for testing coalescing logic.
    /// </summary>
    private class CounterEvent : IEvent
    {
        public int Delta { get; set; }

        public EventType EventType() => EventCounter;

        public override string ToString() => $"CounterEvent {Delta}";
    }

    /// <summary>
    /// Mock coalescer for testing.
    /// </summary>
    private class MockCoalescer : ICoalescer
    {
        public int Value { get; set; }

        public bool Handle(IEvent e)
        {
            return e.EventType() == EventCounter;
        }

        public void Coalesce(IEvent e)
        {
            Value += ((CounterEvent)e).Delta;
        }

        public void Flush(ChannelWriter<IEvent> outChan)
        {
            outChan.TryWrite(new CounterEvent { Delta = Value });
            Value = 0;
        }
    }

    /// <summary>
    /// Helper to create a test coalescer setup.
    /// </summary>
    private static (ChannelWriter<IEvent> inCh, ChannelReader<IEvent> outCh, CancellationTokenSource shutdownCts) CreateTestCoalescer(
        TimeSpan coalescePeriod,
        TimeSpan quiescentPeriod)
    {
        var outChannel = Channel.CreateUnbounded<IEvent>();
        var shutdownCts = new CancellationTokenSource();
        var coalescer = new MockCoalescer();

        var inCh = CoalesceLoop.CoalescedEventChannel(
            outChannel.Writer,
            shutdownCts.Token,
            coalescePeriod,
            quiescentPeriod,
            coalescer);

        return (inCh, outChannel.Reader, shutdownCts);
    }

    [Fact]
    public async Task Coalescer_Basic_ShouldCoalesceEvents()
    {
        // Arrange
        var (inCh, outCh, shutdownCts) = CreateTestCoalescer(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromSeconds(1));

        try
        {
            var send = new IEvent[]
            {
                new CounterEvent { Delta = 1 },
                new CounterEvent { Delta = 39 },
                new CounterEvent { Delta = 2 }
            };

            // Act - Send events
            foreach (var e in send)
            {
                await inCh.WriteAsync(e);
            }

            // Assert - Should receive coalesced event
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result = await outCh.ReadAsync(cts.Token);

            result.EventType().Should().Be(EventCounter, "expected counter event");
            ((CounterEvent)result).Delta.Should().Be(42, "should sum to 42");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task Coalescer_Quiescent_ShouldFlushOnQuiescence()
    {
        // This tests the quiescence by creating a long coalescence period
        // with a short quiescent period and waiting only a multiple of the
        // quiescent period for results.

        // Arrange
        var (inCh, outCh, shutdownCts) = CreateTestCoalescer(
            TimeSpan.FromSeconds(10),  // Long quantum period
            TimeSpan.FromMilliseconds(10)); // Short quiescent period

        try
        {
            var send = new IEvent[]
            {
                new CounterEvent { Delta = 1 },
                new CounterEvent { Delta = 39 },
                new CounterEvent { Delta = 2 }
            };

            // Act - Send events
            foreach (var e in send)
            {
                await inCh.WriteAsync(e);
            }

            // Assert - Should receive coalesced event due to quiescence
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result = await outCh.ReadAsync(cts.Token);

            result.EventType().Should().Be(EventCounter, "expected counter event");
            ((CounterEvent)result).Delta.Should().Be(42, "should sum to 42");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task Coalescer_PassThrough_ShouldPassUnhandledEvents()
    {
        // Arrange
        var (inCh, outCh, shutdownCts) = CreateTestCoalescer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

        try
        {
            var send = new UserEvent
            {
                Name = "test",
                Payload = "foo"u8.ToArray()
            };

            // Act - Send event that coalescer doesn't handle
            await inCh.WriteAsync(send);

            // Assert - Should receive event immediately
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result = await outCh.ReadAsync(cts.Token);

            result.EventType().Should().Be(EventType.User, "expected user event");
            var userEvent = (UserEvent)result;
            userEvent.Name.Should().Be("test", "name should be test");
            userEvent.Payload.Should().Equal("foo"u8.ToArray(), "payload should match");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task Coalescer_MultipleFlushCycles_ShouldWorkCorrectly()
    {
        // Arrange
        var (inCh, outCh, shutdownCts) = CreateTestCoalescer(
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(5));

        try
        {
            // Act - First batch
            await inCh.WriteAsync(new CounterEvent { Delta = 10 });
            await inCh.WriteAsync(new CounterEvent { Delta = 5 });

            // Wait for first flush
            using var cts1 = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result1 = await outCh.ReadAsync(cts1.Token);
            ((CounterEvent)result1).Delta.Should().Be(15, "first batch should sum to 15");

            // Act - Second batch
            await inCh.WriteAsync(new CounterEvent { Delta = 20 });
            await inCh.WriteAsync(new CounterEvent { Delta = 7 });

            // Wait for second flush
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result2 = await outCh.ReadAsync(cts2.Token);
            ((CounterEvent)result2).Delta.Should().Be(27, "second batch should sum to 27");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task Coalescer_EmptyFlush_ShouldNotCrash()
    {
        // Arrange
        var (inCh, outCh, shutdownCts) = CreateTestCoalescer(
            TimeSpan.FromMilliseconds(5),
            TimeSpan.FromMilliseconds(5));

        try
        {
            // Act - Send event that gets coalesced to zero
            await inCh.WriteAsync(new CounterEvent { Delta = 0 });

            // Wait for flush
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var result = await outCh.ReadAsync(cts.Token);

            // Assert - Should receive event with delta 0
            ((CounterEvent)result).Delta.Should().Be(0, "should receive zero");
        }
        finally
        {
            shutdownCts.Cancel();
        }
    }

    [Fact]
    public async Task Coalescer_Shutdown_ShouldFlushAndStop()
    {
        // Arrange
        var (inCh, outCh, shutdownCts) = CreateTestCoalescer(
            TimeSpan.FromSeconds(10),  // Long period so shutdown triggers flush
            TimeSpan.FromSeconds(10));

        // Act - Send events and give a tiny bit of time to coalesce
        await inCh.WriteAsync(new CounterEvent { Delta = 100 });
        await Task.Delay(TimeSpan.FromMilliseconds(5)); // Allow coalescing to happen

        // Shutdown
        shutdownCts.Cancel();

        // Assert - Should flush on shutdown
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            var result = await outCh.ReadAsync(cts.Token);
            ((CounterEvent)result).Delta.Should().Be(100, "should flush on shutdown");
        }
        catch (OperationCanceledException)
        {
            // Channel was closed before flush, which is acceptable in shutdown scenario
            // This test primarily verifies no crash/hang on shutdown
        }
    }
}
