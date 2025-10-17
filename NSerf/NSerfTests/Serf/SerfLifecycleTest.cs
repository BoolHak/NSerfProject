// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.1: Serf Create() and Lifecycle Tests

using Xunit;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Serf lifecycle operations: Create, State transitions, Shutdown, Leave
/// Ported from Go's serf_test.go Phase 9.1 tests
/// </summary>
public class SerfLifecycleTest
{
    /// <summary>
    /// Test: Protocol version validation during Create
    /// Maps to: TestCreate_badProtocolVersion
    /// </summary>
    [Theory]
    [InlineData(NSerf.Serf.Serf.ProtocolVersionMin, false)]
    [InlineData(NSerf.Serf.Serf.ProtocolVersionMax, false)]
    [InlineData(NSerf.Serf.Serf.ProtocolVersionMax + 1, true)]
    [InlineData(NSerf.Serf.Serf.ProtocolVersionMax - 1, false)]
    public async Task Create_WithProtocolVersion_ShouldValidate(byte version, bool shouldFail)
    {
        // Arrange
        var config = new Config
        {
            NodeName = $"test-node-v{version}",
            ProtocolVersion = version,
            MemberlistConfig = new MemberlistConfig
            {
                Name = $"test-node-v{version}",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        // Act & Assert
        if (shouldFail)
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                using var serf = await NSerf.Serf.Serf.CreateAsync(config);
            });
        }
        else
        {
            using var serf = await NSerf.Serf.Serf.CreateAsync(config);
            Assert.NotNull(serf);
            await serf.ShutdownAsync();
        }
    }

    /// <summary>
    /// Test: Serf state transitions through lifecycle
    /// Maps to: TestSerfState
    /// </summary>
    [Fact]
    public async Task Serf_StateTransitions_ShouldFollowLifecycle()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-state-node",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-state-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Assert - Initial state should be Alive
        Assert.Equal(SerfState.SerfAlive, serf.State());

        // Act - Leave the cluster
        await serf.LeaveAsync();

        // Assert - State should be Left
        Assert.Equal(SerfState.SerfLeft, serf.State());

        // Act - Shutdown
        await serf.ShutdownAsync();

        // Assert - State should be Shutdown
        Assert.Equal(SerfState.SerfShutdown, serf.State());
    }

    /// <summary>
    /// Test: Protocol version accessor
    /// Maps to: TestSerfProtocolVersion
    /// </summary>
    [Fact]
    public async Task Serf_ProtocolVersion_ShouldReturnConfiguredVersion()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-protocol-node",
            ProtocolVersion = NSerf.Serf.Serf.ProtocolVersionMax,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-protocol-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act
        var version = serf.ProtocolVersion();

        // Assert
        Assert.Equal(NSerf.Serf.Serf.ProtocolVersionMax, version);

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Test: LocalMember returns correct information
    /// Maps to: TestSerf_LocalMember
    /// </summary>
    [Fact]
    public async Task Serf_LocalMember_ShouldReturnCorrectInfo()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            { "role", "test" },
            { "version", "1.0" }
        };

        var config = new Config
        {
            NodeName = "test-local-member",
            Tags = tags,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-local-member",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act
        var member = serf.LocalMember();

        // Assert
        Assert.Equal(config.NodeName, member.Name);
        Assert.Equal(tags, member.Tags);
        Assert.Equal(MemberStatus.Alive, member.Status);

        // Act - Update tags
        var newTags = new Dictionary<string, string>
        {
            { "foo", "bar" },
            { "test", "ing" }
        };
        await serf.SetTagsAsync(newTags);

        // Act - Get member again
        member = serf.LocalMember();

        // Assert - Tags should be updated
        Assert.Equal(newTags, member.Tags);

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Test: Create with invalid user event size limit
    /// </summary>
    [Fact]
    public async Task Create_WithExcessiveUserEventSize_ShouldFail()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-event-size",
            UserEventSizeLimit = NSerf.Serf.Serf.UserEventSizeLimit + 1,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-event-size",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        });
    }

    /// <summary>
    /// Test: Shutdown is idempotent
    /// </summary>
    [Fact]
    public async Task Serf_MultipleShutdown_ShouldBeIdempotent()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-shutdown-idempotent",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-shutdown-idempotent",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act & Assert - Multiple shutdowns should not throw
        await serf.ShutdownAsync();
        await serf.ShutdownAsync();
        await serf.ShutdownAsync();

        Assert.Equal(SerfState.SerfShutdown, serf.State());
    }

    /// <summary>
    /// Test: Leave followed by Shutdown
    /// </summary>
    [Fact]
    public async Task Serf_LeaveAndShutdown_ShouldWork()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-leave-shutdown",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-leave-shutdown",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);
        
        Assert.Equal(SerfState.SerfAlive, serf.State());

        // Act
        await serf.LeaveAsync();
        Assert.Equal(SerfState.SerfLeft, serf.State());

        await serf.ShutdownAsync();
        Assert.Equal(SerfState.SerfShutdown, serf.State());
    }
}
