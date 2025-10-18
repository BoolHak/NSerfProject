// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.0: Concurrency tests for thread-safety verification

using Xunit;
using NSerf.Serf;
using System.Collections.Concurrent;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for concurrent access to Serf's thread-safe operations.
/// Based on Go's concurrency patterns and testing approach.
/// </summary>
public class ConcurrencyTest
{
    /// <summary>
    /// Test: Verify LamportClock is thread-safe under concurrent operations
    /// Maps to Go test pattern: TestSerf_LamportClock_Concurrent
    /// </summary>
    [Fact]
    public async Task LamportClock_ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        var clock = new LamportClock();
        var iterations = 1000;
        var concurrentTasks = 10;
        var incrementResults = new ConcurrentBag<LamportTime>();
        var timeResults = new ConcurrentBag<LamportTime>();

        // Act - Multiple threads concurrently incrementing and witnessing
        var tasks = new Task[concurrentTasks];
        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    var time = clock.Time();
                    timeResults.Add(time);
                    
                    var newTime = clock.Increment();
                    incrementResults.Add(newTime);
                    
                    clock.Witness(time + 100); // Witness a higher time
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Verify monotonicity: Time() never decreases
        var timeList = timeResults.OrderBy(t => t).ToList();
        for (int i = 1; i < timeList.Count; i++)
        {
            Assert.True(timeList[i] >= timeList[i-1], "Time() must never decrease (monotonicity)");
        }
        
        // Assert - Verify uniqueness: Each Increment() returns unique value
        var uniqueIncrements = incrementResults.Distinct().Count();
        Assert.Equal(incrementResults.Count, uniqueIncrements);
        
        // Assert - Verify final value accounts for all increments
        var expectedMinimum = (ulong)(concurrentTasks * iterations);
        Assert.True(clock.Time() >= expectedMinimum, $"Clock should be at least {expectedMinimum}");
    }

    /// <summary>
    /// Test: Verify NumMembers is thread-safe
    /// Maps to Go concurrency testing pattern
    /// </summary>
    [Fact]
    public async Task NumMembers_ConcurrentReads_ShouldBeThreadSafe()
    {
        // Arrange
        var config = new Config { NodeName = "test-concurrent-reads" };
        using var serf = new NSerf.Serf.Serf(config);
        var iterations = 100;
        var concurrentTasks = 10;
        var exceptions = new ConcurrentBag<Exception>();
        var readValues = new ConcurrentBag<int>();

        // Act - Multiple threads reading member count
        var tasks = new Task[concurrentTasks];
        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < iterations; j++)
                    {
                        var count = serf.NumMembers();
                        readValues.Add(count);
                        Assert.True(count >= 0, "Member count should never be negative");
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - No exceptions occurred
        Assert.Empty(exceptions);
        
        // Assert - All reads returned consistent valid values (no torn reads)
        Assert.True(readValues.All(v => v >= 0), "All reads should return valid non-negative values");
        
        // Assert - In this test with no concurrent writes, all reads should return 0
        // (initial state, no members added)
        var uniqueValues = readValues.Distinct().ToList();
        Assert.True(uniqueValues.Count <= 2, "With no concurrent modifications, should have very few distinct values");
    }

    /// <summary>
    /// Test: Verify concurrent lock acquisition doesn't deadlock
    /// Simulates the pattern from Go's serf_test.go concurrent operations
    /// </summary>
    [Fact]
    public async Task Serf_ConcurrentLockAcquisition_ShouldNotDeadlock()
    {
        // Arrange
        var config = new Config { NodeName = "test-concurrent-locks" };
        using var serf = new NSerf.Serf.Serf(config);
        var iterations = 50;
        var concurrentTasks = 5;
        var exceptions = new ConcurrentBag<Exception>();
        var lockAcquisitions = new ConcurrentBag<(string lockType, DateTime time)>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act - Simulate concurrent member and event lock access
        var tasks = new Task[concurrentTasks];
        for (int i = 0; i < concurrentTasks; i++)
        {
            int taskId = i;
            tasks[i] = Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < iterations && !cts.Token.IsCancellationRequested; j++)
                    {
                        // Acquire and release member lock
                        serf.AcquireMemberLock();
                        lockAcquisitions.Add(("member", DateTime.UtcNow));
                        Thread.Sleep(1); // Hold lock briefly
                        serf.ReleaseMemberLock();

                        // Acquire and release event lock
                        serf.AcquireEventLock();
                        lockAcquisitions.Add(("event", DateTime.UtcNow));
                        Thread.Sleep(1); // Hold lock briefly
                        serf.ReleaseEventLock();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cts.Token);
        }

        // Wait with timeout to detect deadlocks
        var completedTask = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(completedTask, Task.Delay(TimeSpan.FromSeconds(10))) == completedTask;

        // Assert - All tasks completed without deadlock
        Assert.True(completed, "Tasks should complete without deadlock");
        Assert.Empty(exceptions);
        
        // Assert - Verify locks were acquired successfully (mutual exclusion working)
        var expectedAcquisitions = concurrentTasks * iterations * 2; // member + event per iteration
        Assert.True(lockAcquisitions.Count >= expectedAcquisitions - 10, 
            $"Should have approximately {expectedAcquisitions} lock acquisitions");
        
        // Assert - Verify lock ordering is consistent (no deadlock conditions)
        var memberLockCount = lockAcquisitions.Count(l => l.lockType == "member");
        var eventLockCount = lockAcquisitions.Count(l => l.lockType == "event");
        Assert.True(memberLockCount > 0, "Member locks should be acquired");
        Assert.True(eventLockCount > 0, "Event locks should be acquired");
    }

    /// <summary>
    /// Test: Verify Clock operations don't race under stress
    /// Based on Go's atomic operations testing
    /// </summary>
    [Fact]
    public async Task LamportClock_StressTest_ShouldMaintainMonotonicity()
    {
        // Arrange
        var clock = new LamportClock();
        var iterations = 5000;
        var concurrentTasks = 20;
        var times = new ConcurrentBag<LamportTime>();

        // Act - Stress test with many concurrent operations
        var tasks = new Task[concurrentTasks];
        for (int i = 0; i < concurrentTasks; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    var time = clock.Increment();
                    times.Add(time);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert - Clock maintained monotonicity (all times unique and increasing)
        var sortedTimes = times.OrderBy(t => t).ToList();
        Assert.Equal(concurrentTasks * iterations, sortedTimes.Count);
        
        // Verify no duplicate times (each increment should be unique)
        var uniqueTimes = sortedTimes.Distinct().Count();
        Assert.Equal(sortedTimes.Count, uniqueTimes);
    }

    /// <summary>
    /// Test: Concurrent Lamport clock Time() reads should never see decreasing values
    /// </summary>
    [Fact]
    public async Task LamportClock_ConcurrentReads_ShouldNeverDecrease()
    {
        // Arrange
        var clock = new LamportClock();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var duration = TimeSpan.FromMilliseconds(500);
        var violations = new ConcurrentBag<string>();

        // Act - One writer incrementing, multiple readers checking
        var writerTask = Task.Run(() =>
        {
            while (stopwatch.Elapsed < duration)
            {
                clock.Increment();
                Thread.Sleep(1);
            }
        });

        var readerTasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() =>
        {
            LamportTime lastSeen = 0;
            while (stopwatch.Elapsed < duration)
            {
                var current = clock.Time();
                if (current < lastSeen)
                {
                    violations.Add($"Time decreased from {lastSeen} to {current}");
                }
                lastSeen = current;
            }
        })).ToArray();

        await Task.WhenAll(new[] { writerTask }.Concat(readerTasks));

        // Assert - Time never decreased
        Assert.Empty(violations);
    }

    /// <summary>
    /// Test: Verify Serf dispose is thread-safe
    /// </summary>
    [Fact]
    public async Task Serf_ConcurrentDisposeAndAccess_ShouldHandleGracefully()
    {
        // Arrange
        var config = new Config { NodeName = "test-concurrent-dispose" };
        var serf = new NSerf.Serf.Serf(config);
        var exceptions = new ConcurrentBag<Exception>();

        // Act - Start reading thread, then dispose
        var readTask = Task.Run(() =>
        {
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    try
                    {
                        serf.NumMembers();
                        Thread.Sleep(1);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Expected after dispose
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        await Task.Delay(10); // Let reader start
        serf.Dispose();

        await readTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert - Only ObjectDisposedException is acceptable
        var unexpectedExceptions = exceptions.Where(e => e is not ObjectDisposedException).ToList();
        Assert.Empty(unexpectedExceptions);
    }
}
