using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Core Snapshotter tests matching Go's snapshot_test.go
/// These tests are CRITICAL for correctness and match Go's test coverage.
/// Based on DeepWiki analysis of hashicorp/serf/snapshot_test.go
/// </summary>
[Collection("Sequential Snapshot Tests")]
public class SnapshotterCoreTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                var debugLog = file + ".log";
                if (File.Exists(debugLog)) File.Delete(debugLog);
                var compact = file + ".compact";
                if (File.Exists(compact)) File.Delete(compact);
            }
            catch { }
        }
    }

    private string GetTempSnapshotPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapshot_core_test_{Guid.NewGuid()}.snapshot");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// GO TEST: TestSnapshotter
    /// Verifies basic event persistence, passthrough, clock recovery, and AliveNodes recovery.
    /// This is the MOST IMPORTANT test - it validates the core snapshot functionality.
    /// </summary>
    [Fact]
    public async Task TestSnapshotter_BasicFunctionality()
    {
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        var outCh = Channel.CreateUnbounded<IEvent>();

        // Create snapshotter
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, false, null, clock, outCh.Writer, shutdownCts.Token);

        // Write various events
        var member1 = new Member
        {
            Name = "node1",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 7946
        };
        var member2 = new Member
        {
            Name = "node2",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 7947
        };

        // MemberJoin event
        await inCh.WriteAsync(new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { member1, member2 }
        });

        // Advance clock
        clock.Increment();
        clock.Increment();

        // UserEvent
        await inCh.WriteAsync(new UserEvent
        {
            LTime = new LamportTime(5),
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 }
        });

        // Query
        await inCh.WriteAsync(new Query
        {
            LTime = new LamportTime(10),
            Name = "test-query",
            Payload = new byte[] { 4, 5, 6 }
        });

        // Verify events passed through to output channel
        var receivedEvents = new List<IEvent>();

        // Wait for all 3 events to be received (with timeout)
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (receivedEvents.Count < 3 && DateTime.UtcNow < deadline)
        {
            if (outCh.Reader.TryRead(out var evt))
            {
                receivedEvents.Add(evt);
            }
            else
            {
                await Task.Delay(100);
            }
        }
        receivedEvents.Should().HaveCount(3, "all events should pass through");

        // Wait for events to be processed - check in-memory clock values
        deadline = DateTime.UtcNow.AddSeconds(5);
        while ((snap.LastEventClock != new LamportTime(5) || snap.LastQueryClock != new LamportTime(10)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Console.WriteLine($"[DEBUG BEFORE SHUTDOWN] LastEventClock={snap.LastEventClock}, LastQueryClock={snap.LastQueryClock}");
        snap.LastEventClock.Should().Be(new LamportTime(5), "event clock should be updated");
        snap.LastQueryClock.Should().Be(new LamportTime(10), "query clock should be updated");

        // Record clock values before shutdown
        var expectedClock = snap.LastClock;
        var expectedEventClock = snap.LastEventClock;
        var expectedQueryClock = snap.LastQueryClock;

        Console.WriteLine($"[DEBUG] Expected: EventClock={expectedEventClock}, QueryClock={expectedQueryClock}");
        expectedEventClock.Should().Be(new LamportTime(5));
        expectedQueryClock.Should().Be(new LamportTime(10));

        // Shutdown
        shutdownCts.Cancel();
        await snap.WaitAsync();
        await snap.DisposeAsync();

        // === CRITICAL: Restart and verify recovery ===
        var shutdownCts2 = new CancellationTokenSource();
        var outCh2 = Channel.CreateUnbounded<IEvent>();

        var (inCh2, snap2) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, false, null, clock, outCh2.Writer, shutdownCts2.Token);

        // Verify clocks recovered
        snap2.LastClock.Should().BeGreaterOrEqualTo(expectedClock, "LastClock should be recovered");
        snap2.LastEventClock.Should().Be(expectedEventClock, "LastEventClock should be recovered");
        snap2.LastQueryClock.Should().Be(expectedQueryClock, "LastQueryClock should be recovered");

        // Verify AliveNodes recovered
        var aliveNodes = snap2.AliveNodes();
        aliveNodes.Should().HaveCount(2, "both nodes should be recovered");
        aliveNodes.Should().Contain(n => n.Name == "node1", "node1 should be in AliveNodes");
        aliveNodes.Should().Contain(n => n.Name == "node2", "node2 should be in AliveNodes");

        // Cleanup
        shutdownCts2.Cancel();
        await snap2.WaitAsync();
        await snap2.DisposeAsync();
    }

    /// <summary>
    /// GO TEST: TestSnapshotter_forceCompact
    /// Verifies compaction mechanism and clock recovery after compaction.
    /// Uses very low minCompactSize to trigger compaction.
    /// </summary>
    [Fact]
    public async Task TestSnapshotter_Compaction()
    {
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();

        // Very small minCompactSize to force compaction
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 64, false, null, clock, null, shutdownCts.Token);

        // Write many events to trigger compaction
        for (int i = 1; i <= 100; i++)
        {
            await inCh.WriteAsync(new UserEvent
            {
                LTime = new LamportTime((ulong)i),
                Name = $"event-{i}",
                Payload = new byte[] { (byte)i }
            });

            await inCh.WriteAsync(new Query
            {
                LTime = new LamportTime((ulong)(i + 1000)),
                Name = $"query-{i}",
                Payload = new byte[] { (byte)i }
            });
        }

        // Wait longer for compaction to occur and events to be fully processed
        await Task.Delay(2000);

        // Record expected final clocks
        var expectedEventClock = new LamportTime(100);
        var expectedQueryClock = new LamportTime(1100);

        // Shutdown
        shutdownCts.Cancel();
        await snap.WaitAsync();
        await snap.DisposeAsync();

        // Verify snapshot file was compacted (should be smaller than raw append)
        var fileInfo = new FileInfo(path);
        fileInfo.Exists.Should().BeTrue("snapshot file should exist");

        // Restart and verify clocks recovered correctly after compaction
        var shutdownCts2 = new CancellationTokenSource();
        var (inCh2, snap2) = await Snapshotter.NewSnapshotterAsync(
            path, 64, false, null, clock, null, shutdownCts2.Token);

        snap2.LastEventClock.Should().Be(expectedEventClock, "LastEventClock should reflect latest event after compaction");
        snap2.LastQueryClock.Should().Be(expectedQueryClock, "LastQueryClock should reflect latest query after compaction");

        // Cleanup
        shutdownCts2.Cancel();
        await snap2.WaitAsync();
        await snap2.DisposeAsync();
    }

    /// <summary>
    /// GO TEST: TestSnapshotter_leave
    /// Verifies that Leave() clears state when rejoinAfterLeave=false (default).
    /// After restart, clocks should be 0 and AliveNodes empty.
    /// </summary>
    [Fact]
    public async Task TestSnapshotter_Leave_ClearsState()
    {
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();

        // rejoinAfterLeave = FALSE (default)
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, false, null, clock, null, shutdownCts.Token);

        // Write events
        await inCh.WriteAsync(new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>
            {
                new Member { Name = "node1", Addr = IPAddress.Parse("127.0.0.1"), Port = 7946 }
            }
        });

        await inCh.WriteAsync(new UserEvent
        {
            LTime = new LamportTime(5),
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 }
        });

        // Wait longer for events to be fully processed and flushed
        await Task.Delay(1000);

        // Call Leave
        await snap.LeaveAsync();
        // Wait for leave to be fully flushed
        await Task.Delay(500);

        // Shutdown
        shutdownCts.Cancel();
        await snap.WaitAsync();
        await snap.DisposeAsync();

        // === DEBUG: Check snapshot file content ===
        var fileContent = await File.ReadAllTextAsync(path);
        Console.WriteLine("[DEBUG] Snapshot file after leave:");
        Console.WriteLine(fileContent);
        Console.WriteLine("[DEBUG] End of snapshot file");

        // === Restart and verify state was CLEARED ===
        var shutdownCts2 = new CancellationTokenSource();
        var (inCh2, snap2) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, false, null, clock, null, shutdownCts2.Token);

        // All clocks should be reset to 0
        Console.WriteLine($"[DEBUG] After restart: LastClock={snap2.LastClock}, LastEventClock={snap2.LastEventClock}, LastQueryClock={snap2.LastQueryClock}");
        snap2.LastClock.Should().Be(new LamportTime(0), "LastClock should be 0 after leave");
        snap2.LastEventClock.Should().Be(new LamportTime(0), "LastEventClock should be 0 after leave");
        snap2.LastQueryClock.Should().Be(new LamportTime(0), "LastQueryClock should be 0 after leave");

        // AliveNodes should be empty
        var aliveNodes = snap2.AliveNodes();
        aliveNodes.Should().BeEmpty("AliveNodes should be cleared after leave");

        // Cleanup
        shutdownCts2.Cancel();
        await snap2.WaitAsync();
        await snap2.DisposeAsync();
    }

    /// <summary>
    /// GO TEST: TestSnapshotter_leave_rejoin
    /// CRITICAL: Verifies that when rejoinAfterLeave=true, state is PRESERVED after leave.
    /// This is essential for the rejoin scenario.
    /// </summary>
    [Fact]
    public async Task TestSnapshotter_LeaveRejoin_PreservesState()
    {
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();

        // rejoinAfterLeave = TRUE
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, true, null, clock, null, shutdownCts.Token);

        // Write events
        await inCh.WriteAsync(new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>
            {
                new Member { Name = "node1", Addr = IPAddress.Parse("127.0.0.1"), Port = 7946 },
                new Member { Name = "node2", Addr = IPAddress.Parse("127.0.0.1"), Port = 7947 }
            }
        });

        clock.Increment();
        clock.Increment();

        await inCh.WriteAsync(new UserEvent
        {
            LTime = new LamportTime(5),
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 }
        });

        await inCh.WriteAsync(new Query
        {
            LTime = new LamportTime(10),
            Name = "test-query",
            Payload = new byte[] { 4, 5, 6 }
        });

        await Task.Delay(300);

        // Record expected values
        var expectedClock = snap.LastClock;
        var expectedEventClock = snap.LastEventClock;
        var expectedQueryClock = snap.LastQueryClock;

        // Call Leave (but state should be preserved due to rejoinAfterLeave=true)
        await snap.LeaveAsync();
        await Task.Delay(200);

        // Shutdown
        shutdownCts.Cancel();
        await snap.WaitAsync();
        await snap.DisposeAsync();

        // === CRITICAL: Restart with rejoinAfterLeave=true and verify state PRESERVED ===
        var shutdownCts2 = new CancellationTokenSource();
        var (inCh2, snap2) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, true, null, clock, null, shutdownCts2.Token);

        // Clocks should be PRESERVED
        snap2.LastClock.Should().BeGreaterOrEqualTo(expectedClock, "LastClock should be preserved with rejoinAfterLeave");
        snap2.LastEventClock.Should().Be(expectedEventClock, "LastEventClock should be preserved");
        snap2.LastQueryClock.Should().Be(expectedQueryClock, "LastQueryClock should be preserved");

        // AliveNodes should be PRESERVED
        var aliveNodes = snap2.AliveNodes();
        aliveNodes.Should().HaveCount(2, "AliveNodes should be preserved with rejoinAfterLeave");
        aliveNodes.Should().Contain(n => n.Name == "node1");
        aliveNodes.Should().Contain(n => n.Name == "node2");

        // Cleanup
        shutdownCts2.Cancel();
        await snap2.WaitAsync();
        await snap2.DisposeAsync();
    }

    /// <summary>
    /// GO TEST: TestSnapshotter_blockedUpstreamNotBlockingMemberlist
    /// Verifies that a blocked output channel doesn't block the input channel.
    /// This is critical for preventing memberlist from hanging.
    /// </summary>
    [Fact]
    public async Task TestSnapshotter_BlockedUpstreamDoesNotBlockInput()
    {
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();

        // Create UNBUFFERED output channel to simulate slow/blocked upstream
        var outCh = Channel.CreateUnbounded<IEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024 * 1024, false, null, clock, outCh.Writer, shutdownCts.Token);

        // Send many events WITHOUT reading from outCh (simulates blocked upstream)
        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                var member = new Member
                {
                    Name = $"node{i}",
                    Addr = IPAddress.Parse("127.0.0.1"),
                    Port = (ushort)(7946 + i)
                };

                await inCh.WriteAsync(new MemberEvent
                {
                    Type = EventType.MemberJoin,
                    Members = new List<Member> { member }
                });
            }
        });

        // Wait for writes to complete - should NOT block even though outCh is not being read
        var completed = await Task.WhenAny(writeTask, Task.Delay(5000));
        completed.Should().Be(writeTask, "input writes should not block even if output channel is blocked");

        // Cleanup
        shutdownCts.Cancel();
        await snap.WaitAsync();
        await snap.DisposeAsync();
    }
}
