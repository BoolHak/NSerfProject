// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Xunit;
using SerfNamespace = NSerf.Serf;
using NSerf.Serf;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for BackgroundTasks (Reaper and Reconnect)
/// TDD implementation following BACKGROUNDTASKS_TDD_CHECKLIST.md
/// </summary>
public class BackgroundTasksTest : IDisposable
{
    private readonly List<SerfNamespace.Serf> _serfs = new();
    
    public void Dispose()
    {
        foreach (var serf in _serfs)
        {
            try
            {
                serf.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private Config TestConfig()
    {
        var config = TestHelpers.CreateTestConfig();
        // Convert SerfConfig to Config
        return new Config
        {
            NodeName = config.NodeName,
            MemberlistConfig = config.MemberlistConfig,
            ReapInterval = TimeSpan.FromMilliseconds(100),
            ReconnectInterval = TimeSpan.FromMilliseconds(100),
            ReconnectTimeout = TimeSpan.FromMilliseconds(500),
            TombstoneTimeout = TimeSpan.FromSeconds(3600)
        };
    }

    // ==================== Phase 1: Critical Fixes ====================

    /// <summary>
    /// Test 1: Background tasks should stop cleanly during shutdown
    /// Related to Fix #1: Task Shutdown Coordination
    /// </summary>
    [Fact]
    public async Task BackgroundTasks_ShouldStopDuringShutdown()
    {
        // Arrange
        var config = TestConfig();
        config.ReapInterval = TimeSpan.FromMilliseconds(100);
        config.ReconnectInterval = TimeSpan.FromMilliseconds(100);
        var serf = await SerfNamespace.Serf.CreateAsync(config);
        _serfs.Add(serf);
        
        // Let tasks run for a bit to ensure they started
        await Task.Delay(200);
        
        // Act - shutdown and measure time
        var sw = Stopwatch.StartNew();
        await serf.ShutdownAsync();
        sw.Stop();
        
        // Assert - shutdown should complete quickly (not hang)
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, 
            "shutdown should complete within 2 seconds");
    }

    /// <summary>
    /// Test 2: Reaper should remove expired failed members
    /// Related to Fix #2: EraseNode Lock Documentation
    /// </summary>
    [Fact]
    public async Task Reaper_ShouldRemoveExpiredMembers()
    {
        // Arrange
        var config = TestConfig();
        config.ReapInterval = TimeSpan.FromMilliseconds(100);
        config.ReconnectTimeout = TimeSpan.FromMilliseconds(200);
        var serf = await SerfNamespace.Serf.CreateAsync(config);
        _serfs.Add(serf);
        
        // Create an expired member (left 300ms ago, timeout is 200ms)
        var expiredMember = new MemberInfo
        {
            Name = "expired-node",
            LeaveTime = DateTimeOffset.UtcNow.AddMilliseconds(-300),
            Member = new Member 
            { 
                Name = "expired-node", 
                Addr = IPAddress.Parse("127.0.0.1"), 
                Port = 9999,
                Status = MemberStatus.Failed
            }
        };
        
        // Add to failed members list
        serf.FailedMembers.Add(expiredMember);
        serf.MemberStates["expired-node"] = expiredMember;
        
        // Act - wait for reaper to run (100ms interval + processing time)
        await Task.Delay(300);
        
        // Assert - expired member should be removed
        serf.FailedMembers.Should().NotContain(m => m.Name == "expired-node",
            "reaper should remove expired members");
        serf.MemberStates.Should().NotContainKey("expired-node",
            "member should be erased from state");
    }

    /// <summary>
    /// Test 3: Reconnect operation should cancel on shutdown
    /// Related to Fix #3: Wrong CancellationToken
    /// </summary>
    [Fact]
    public async Task Reconnect_ShouldCancelOnShutdown()
    {
        // Arrange
        var config = TestConfig();
        config.ReconnectInterval = TimeSpan.FromMilliseconds(50);
        var serf = await SerfNamespace.Serf.CreateAsync(config);
        _serfs.Add(serf);
        
        // Add failed member to trigger reconnect attempts
        var failedMember = new MemberInfo
        {
            Name = "failed-node",
            LeaveTime = DateTimeOffset.UtcNow,
            Member = new Member
            {
                Name = "failed-node",
                Addr = IPAddress.Parse("127.0.0.1"),
                Port = 9999,  // Non-existent port
                Status = MemberStatus.Failed
            }
        };
        serf.FailedMembers.Add(failedMember);
        
        // Let reconnect task potentially start
        await Task.Delay(30);
        
        // Act - shutdown should complete quickly even if reconnect is in progress
        var sw = Stopwatch.StartNew();
        await serf.ShutdownAsync();
        sw.Stop();
        
        // Assert - shutdown cancels reconnect quickly
        sw.ElapsedMilliseconds.Should().BeLessThan(1000,
            "shutdown should cancel reconnect operation quickly");
    }

    /// <summary>
    /// Test 5: Reaper should preserve non-expired members
    /// Related to Fix #5: List Recreation
    /// </summary>
    [Fact]
    public async Task Reaper_ShouldPreserveNonExpiredMembers()
    {
        // Arrange
        var config = TestConfig();
        config.ReapInterval = TimeSpan.FromMilliseconds(100);
        config.ReconnectTimeout = TimeSpan.FromSeconds(10);  // Long timeout
        var serf = await SerfNamespace.Serf.CreateAsync(config);
        _serfs.Add(serf);
        
        // Create a recently failed member (not expired)
        var recentMember = new MemberInfo
        {
            Name = "recent-node",
            LeaveTime = DateTimeOffset.UtcNow.AddMilliseconds(-50),  // Only 50ms ago
            Member = new Member
            {
                Name = "recent-node",
                Addr = IPAddress.Parse("127.0.0.1"),
                Port = 9999,
                Status = MemberStatus.Failed
            }
        };
        
        serf.FailedMembers.Add(recentMember);
        serf.MemberStates["recent-node"] = recentMember;
        
        // Act - wait for reaper to run
        await Task.Delay(300);
        
        // Assert - recent member should still be present
        serf.FailedMembers.Should().Contain(m => m.Name == "recent-node",
            "reaper should preserve non-expired members");
        serf.MemberStates.Should().ContainKey("recent-node",
            "non-expired member should remain in state");
    }
}
