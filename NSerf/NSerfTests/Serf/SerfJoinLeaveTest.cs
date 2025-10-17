// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.2: Join/Leave Operations Tests
// Ported from: github.com/hashicorp/serf/serf/serf_test.go

using Xunit;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using FluentAssertions;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Serf Join and Leave operations.
/// Based on serf_test.go Phase 9.2 tests.
/// </summary>
public class SerfJoinLeaveTest
{
    /// <summary>
    /// Test: Basic join and leave operations
    /// Maps to: TestSerf_joinLeave
    /// </summary>
    [Fact]
    public async Task Serf_JoinAndLeave_ShouldWork()
    {
        // Arrange
        var config1 = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0, // Auto-assign
                AdvertiseAddr = "127.0.0.1"
            }
        };

        var config2 = new Config
        {
            NodeName = "node2",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node2",
                BindAddr = "127.0.0.1",
                BindPort = 0, // Auto-assign
                AdvertiseAddr = "127.0.0.1"
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config1);
        using var s2 = await NSerf.Serf.Serf.CreateAsync(config2);

        // Both should start with 1 member (themselves)
        s1.NumMembers().Should().Be(1);
        s2.NumMembers().Should().Be(1);

        // Act - s1 joins s2
        var s2Addr = $"127.0.0.1:{config2.MemberlistConfig.BindPort}";
        var numJoined = await s1.JoinAsync(new[] { s2Addr }, ignoreOld: false);

        // Assert - Join should succeed
        numJoined.Should().BeGreaterThan(0);

        // TODO: Wait for convergence and verify both nodes see 2 members
        // This requires implementing the event handlers first

        // Act - s1 leaves
        await s1.LeaveAsync();

        // Assert - State should be left
        s1.State().Should().Be(SerfState.SerfLeft);

        await s1.ShutdownAsync();
        await s2.ShutdownAsync();
    }

    /// <summary>
    /// Test: Join with ignoreOld flag
    /// Maps to: TestSerf_Join_IgnoreOld
    /// </summary>
    [Fact]
    public async Task Serf_JoinWithIgnoreOld_ShouldIgnorePreviousEvents()
    {
        // Arrange
        var config1 = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        var config2 = new Config
        {
            NodeName = "node2",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config1);
        using var s2 = await NSerf.Serf.Serf.CreateAsync(config2);

        // Act - Join with ignoreOld=true
        var s2Addr = $"127.0.0.1:{config2.MemberlistConfig.BindPort}";
        var numJoined = await s1.JoinAsync(new[] { s2Addr }, ignoreOld: true);

        // Assert
        numJoined.Should().BeGreaterThan(0);
        s1.EventJoinIgnore.Should().BeFalse(); // Should be reset after join

        await s1.ShutdownAsync();
        await s2.ShutdownAsync();
    }

    /// <summary>
    /// Test: Cannot join after leaving
    /// </summary>
    [Fact]
    public async Task Serf_JoinAfterLeave_ShouldFail()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Leave first
        await s1.LeaveAsync();

        // Assert - Join should fail
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await s1.JoinAsync(new[] { "127.0.0.1:7946" }, ignoreOld: false);
        });

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: Cannot join after shutdown
    /// </summary>
    [Fact]
    public async Task Serf_JoinAfterShutdown_ShouldFail()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Shutdown first
        await s1.ShutdownAsync();

        // Assert - Join should fail
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await s1.JoinAsync(new[] { "127.0.0.1:7946" }, ignoreOld: false);
        });
    }

    /// <summary>
    /// Test: Join with empty address list should fail
    /// </summary>
    [Fact]
    public async Task Serf_JoinWithEmptyAddresses_ShouldFail()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await s1.JoinAsync(Array.Empty<string>(), ignoreOld: false);
        });

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: Join with null address list should fail
    /// </summary>
    [Fact]
    public async Task Serf_JoinWithNullAddresses_ShouldFail()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await s1.JoinAsync(null!, ignoreOld: false);
        });

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: Multiple leave calls should be idempotent
    /// </summary>
    [Fact]
    public async Task Serf_MultipleLeave_ShouldBeIdempotent()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Leave multiple times
        await s1.LeaveAsync();
        await s1.LeaveAsync();
        await s1.LeaveAsync();

        // Assert - State should be left
        s1.State().Should().Be(SerfState.SerfLeft);

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: Leave transitions through correct states
    /// </summary>
    [Fact]
    public async Task Serf_Leave_ShouldTransitionStates()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify initial state
        s1.State().Should().Be(SerfState.SerfAlive);

        // Act - Leave
        await s1.LeaveAsync();

        // Assert - Should be left (we skip Leaving in current implementation)
        s1.State().Should().Be(SerfState.SerfLeft);

        await s1.ShutdownAsync();
    }

    /// <summary>
    /// Test: Shutdown after leave should work
    /// </summary>
    [Fact]
    public async Task Serf_ShutdownAfterLeave_ShouldWork()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "node1",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act
        await s1.LeaveAsync();
        s1.State().Should().Be(SerfState.SerfLeft);

        await s1.ShutdownAsync();

        // Assert
        s1.State().Should().Be(SerfState.SerfShutdown);
    }

    /// <summary>
    /// Test: LocalMember should return correct info after creation
    /// </summary>
    [Fact]
    public async Task Serf_LocalMember_ShouldMatchConfig()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>
            {
                { "role", "test" },
                { "datacenter", "dc1" }
            },
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 7946
            }
        };

        using var s1 = await NSerf.Serf.Serf.CreateAsync(config);

        // Act
        var member = s1.LocalMember();

        // Assert
        member.Name.Should().Be("test-node");
        member.Tags.Should().ContainKey("role");
        member.Tags["role"].Should().Be("test");
        member.Status.Should().Be(MemberStatus.Alive);

        await s1.ShutdownAsync();
    }
}
