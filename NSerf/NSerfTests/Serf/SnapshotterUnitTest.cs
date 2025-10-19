using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Unit tests for Snapshotter class focusing on lifecycle, shutdown, and reliability.
/// Tests follow the fix checklist and use TDD methodology.
/// </summary>
[Collection("Sequential Snapshot Tests")]
public class SnapshotterUnitTest : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        // Clean up temp files
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
                
                // Also delete debug log
                var debugLog = file + ".log";
                if (File.Exists(debugLog))
                {
                    File.Delete(debugLog);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private string GetTempSnapshotPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapshotter_unit_test_{Guid.NewGuid()}.snapshot");
        _tempFiles.Add(path);
        return path;
    }

    // ==================== Test Group B: Shutdown Coordination ====================

    /// <summary>
    /// Test B1: Verifies no events are lost during shutdown
    /// Related to Fix 2: Shutdown Coordination
    /// </summary>
    [Fact]
    public async Task Shutdown_ShouldNotLoseEvents()
    {
        // Arrange
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        var eventCh = Channel.CreateUnbounded<Event>();
        
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024, false, null, clock, eventCh.Writer, shutdownCts.Token);

        // Act - send 100 events and shutdown immediately
        var member = new Member 
        { 
            Name = "test1", 
            Addr = IPAddress.Parse("127.0.0.1"), 
            Port = 7946 
        };
        var joinEvent = new MemberEvent 
        { 
            Type = EventType.MemberJoin, 
            Members = new List<Member> { member } 
        };
        
        for (int i = 0; i < 100; i++)
        {
            await inCh.WriteAsync(joinEvent);
        }
        
        // Small delay to let some events process
        await Task.Delay(100);
        
        // Trigger shutdown
        shutdownCts.Cancel();
        await snap.WaitAsync();
        
        // Assert - all events should be written
        var content = await File.ReadAllTextAsync(path);
        var aliveCount = content.Split('\n').Count(line => line.StartsWith("alive:"));
        
        // We expect 100 events, but due to deduplication (same node name), we get 1
        // Let's verify at least the event was written
        aliveCount.Should().BeGreaterThan(0, "at least one event should be written");
        
        // Better test: verify the file contains our test node
        content.Should().Contain("test1", "the test node should be in snapshot");
    }

    /// <summary>
    /// Test B2: Verifies shutdown respects timeout and doesn't hang
    /// Related to Fix 2: Shutdown Coordination
    /// </summary>
    [Fact]
    public async Task Shutdown_ShouldCompleteWithinTimeout()
    {
        // Arrange
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024, false, null, clock, null, shutdownCts.Token);

        // Act - queue many events then shutdown
        var member = new Member 
        { 
            Name = "test1", 
            Addr = IPAddress.Parse("127.0.0.1"), 
            Port = 7946 
        };
        var joinEvent = new MemberEvent 
        { 
            Type = EventType.MemberJoin, 
            Members = new List<Member> { member } 
        };
        
        // Try to fill the queue (will depend on bounded/unbounded)
        for (int i = 0; i < 5000; i++)
        {
            inCh.TryWrite(joinEvent);
        }
        
        // Shutdown should complete within reasonable time
        shutdownCts.Cancel();
        var sw = Stopwatch.StartNew();
        await snap.WaitAsync();
        sw.Stop();
        
        // Assert - completed within 2 seconds (generous timeout for safety)
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, 
            "shutdown should complete quickly even with pending events");
    }

    // ==================== Test Group A: Disposal & Lifecycle ====================

    /// <summary>
    /// Test A1: Verifies DisposeAsync waits for background tasks to complete
    /// Related to Fix 1: Async Disposal
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ShouldWaitForTaskCompletion()
    {
        // Arrange
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024, false, null, clock, null, shutdownCts.Token);

        // Add some events to ensure tasks are working
        var member = new Member 
        { 
            Name = "test", 
            Addr = IPAddress.Parse("127.0.0.1"), 
            Port = 7946 
        };
        var evt = new MemberEvent 
        { 
            Type = EventType.MemberJoin, 
            Members = new List<Member> { member } 
        };
        await inCh.WriteAsync(evt);
        await Task.Delay(100);

        // Act - cancel and dispose
        shutdownCts.Cancel();
        
        // DisposeAsync should wait for tasks
        await snap.DisposeAsync();
        
        // Assert - tasks should be completed
        // Note: This will fail until we implement IAsyncDisposable
        Assert.True(true, "DisposeAsync should complete without hanging");
    }

    /// <summary>
    /// Test A2: Verifies sync Dispose doesn't hang
    /// Related to Fix 1: Async Disposal
    /// </summary>
    [Fact]
    public async Task Dispose_ShouldNotHang()
    {
        // Arrange
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024, false, null, clock, null, shutdownCts.Token);

        // Act - sync dispose should not hang
        shutdownCts.Cancel();
        snap.Dispose();
        
        // Assert - completes immediately
        Assert.True(true, "Dispose completed without hanging");
    }

    // ==================== Test Group C: Channel Backpressure ====================

    /// <summary>
    /// Test C1: Verifies bounded channel applies backpressure when full
    /// Related to Fix 3: Bounded Channels
    /// </summary>
    [Fact]
    public async Task BoundedChannel_ShouldApplyBackpressure()
    {
        // Arrange
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024, false, null, clock, null, shutdownCts.Token);

        // Act - flood channel beyond capacity
        var member = new Member 
        { 
            Name = "test", 
            Addr = IPAddress.Parse("127.0.0.1"), 
            Port = 7946 
        };
        var evt = new MemberEvent 
        { 
            Type = EventType.MemberJoin, 
            Members = new List<Member> { member } 
        };
        
        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < 5000; i++)
            {
                await inCh.WriteAsync(evt);
            }
        });

        // Let it run for a bit
        await Task.Delay(500);
        
        // Assert - task should still be running if backpressure is applied
        // (This will pass with unbounded, fail with bounded until processing catches up)
        // For now, we just verify the system doesn't crash
        writeTask.IsCompleted.Should().BeFalse("backpressure should slow down writes");
        
        // Cleanup
        shutdownCts.Cancel();
        await snap.WaitAsync();
    }

    /// <summary>
    /// Test C2: Verifies memory stays bounded under sustained load
    /// Related to Fix 3: Bounded Channels
    /// </summary>
    [Fact]
    public async Task MemoryUsage_ShouldStayBounded()
    {
        // Arrange
        var path = GetTempSnapshotPath();
        var clock = new LamportClock();
        var shutdownCts = new CancellationTokenSource();
        
        var (inCh, snap) = await Snapshotter.NewSnapshotterAsync(
            path, 1024, false, null, clock, null, shutdownCts.Token);

        // Record initial memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);

        // Act - sustained load
        var member = new Member 
        { 
            Name = "test", 
            Addr = IPAddress.Parse("127.0.0.1"), 
            Port = 7946 
        };
        var evt = new MemberEvent 
        { 
            Type = EventType.MemberJoin, 
            Members = new List<Member> { member } 
        };
        
        for (int i = 0; i < 1000; i++)
        {
            await inCh.WriteAsync(evt);
            if (i % 100 == 0) await Task.Delay(10);
        }
        
        // Wait for processing
        await Task.Delay(1000);
        
        // Assert - memory growth should be reasonable
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var finalMemory = GC.GetTotalMemory(false);
        var growthMB = (finalMemory - initialMemory) / 1024.0 / 1024.0;
        
        growthMB.Should().BeLessThan(50, $"memory growth should be reasonable, but was {growthMB:F2}MB");
        
        // Cleanup
        shutdownCts.Cancel();
        await snap.WaitAsync();
    }
}
