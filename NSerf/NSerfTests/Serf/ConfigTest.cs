// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/config_test.go

using NSerf.Serf;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Config and configuration defaults.
/// Accurately ported from Go's config_test.go
/// </summary>
public class ConfigTest
{
    [Fact]
    public void DefaultConfig_ShouldHaveProtocolVersion4()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert - Matches Go test exactly
        config.ProtocolVersion.Should().Be(4, "default protocol version should be 4");
    }

    [Fact]
    public void DefaultConfig_ShouldHaveReasonableDefaults()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert - Verify all defaults match Go implementation
        config.NodeName.Should().NotBeNullOrEmpty("NodeName should be set to hostname");
        config.BroadcastTimeout.Should().Be(TimeSpan.FromSeconds(5));
        config.LeavePropagateDelay.Should().Be(TimeSpan.FromSeconds(1));
        config.EventBuffer.Should().Be(512);
        config.QueryBuffer.Should().Be(512);
        config.ProtocolVersion.Should().Be(4);
        config.ReapInterval.Should().Be(TimeSpan.FromSeconds(15));
        config.RecentIntentTimeout.Should().Be(TimeSpan.FromMinutes(5));
        config.ReconnectInterval.Should().Be(TimeSpan.FromSeconds(30));
        config.ReconnectTimeout.Should().Be(TimeSpan.FromHours(24));
        config.QueueCheckInterval.Should().Be(TimeSpan.FromSeconds(30));
        config.QueueDepthWarning.Should().Be(128);
        config.MaxQueueDepth.Should().Be(4096);
        config.TombstoneTimeout.Should().Be(TimeSpan.FromHours(24));
        config.FlapTimeout.Should().Be(TimeSpan.FromSeconds(60));
        config.QueryTimeoutMult.Should().Be(16);
        config.QueryResponseSizeLimit.Should().Be(1024);
        config.QuerySizeLimit.Should().Be(1024);
        config.EnableNameConflictResolution.Should().BeTrue();
        config.DisableCoordinates.Should().BeFalse();
        config.ValidateNodeNames.Should().BeFalse();
        config.UserEventSizeLimit.Should().Be(512);
    }

    [Fact]
    public void DefaultConfig_ShouldHaveMemberlistConfig()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert
        config.MemberlistConfig.Should().NotBeNull("MemberlistConfig should be initialized");
    }

    [Fact]
    public void DefaultConfig_CoalescePeriods_ShouldBeZero()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert - Coalescence is disabled by default
        config.CoalescePeriod.Should().Be(TimeSpan.Zero, "coalescence is disabled by default");
        config.QuiescentPeriod.Should().Be(TimeSpan.Zero);
        config.UserCoalescePeriod.Should().Be(TimeSpan.Zero);
        config.UserQuiescentPeriod.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Config_Init_ShouldInitializeTags()
    {
        // Arrange
        var config = new Config { Tags = null! };

        // Act
        config.Init();

        // Assert
        config.Tags.Should().NotBeNull("Init should initialize Tags dictionary");
        config.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Config_Init_ShouldInitializeMessageDropper()
    {
        // Arrange
        var config = new Config { MessageDropper = null };

        // Act
        config.Init();

        // Assert
        config.MessageDropper.Should().NotBeNull("Init should initialize MessageDropper");
        config.MessageDropper!(MessageType.Join).Should().BeFalse("default MessageDropper returns false");
    }

    [Fact]
    public void Config_Tags_CanBeModified()
    {
        // Arrange
        var config = Config.DefaultConfig();

        // Act
        config.Tags["role"] = "web";
        config.Tags["dc"] = "us-east-1";

        // Assert
        config.Tags.Should().HaveCount(2);
        config.Tags["role"].Should().Be("web");
        config.Tags["dc"].Should().Be("us-east-1");
    }

    [Fact]
    public void ProtocolVersionMap_ShouldHaveCorrectMappings()
    {
        // Arrange & Act & Assert - Verify protocol version mappings
        ProtocolVersionMap.Mapping[5].Should().Be(2, "Serf v5 uses Memberlist v2");
        ProtocolVersionMap.Mapping[4].Should().Be(2, "Serf v4 uses Memberlist v2");
        ProtocolVersionMap.Mapping[3].Should().Be(2, "Serf v3 uses Memberlist v2");
        ProtocolVersionMap.Mapping[2].Should().Be(2, "Serf v2 uses Memberlist v2");
    }

    [Fact]
    public void ProtocolVersionMap_ShouldHaveMinMaxConstants()
    {
        // Arrange & Act & Assert
        ProtocolVersionMap.ProtocolVersionMin.Should().Be(2);
        ProtocolVersionMap.ProtocolVersionMax.Should().Be(5);
    }

    [Fact]
    public void Config_OptionalFields_ShouldBeNullByDefault()
    {
        // Arrange & Act
        var config = new Config();

        // Assert - Optional fields start as null
        config.EventCh.Should().BeNull();
        config.SnapshotPath.Should().BeNull();
        config.KeyringFile.Should().BeNull();
        config.Merge.Should().BeNull();
        config.ReconnectTimeoutOverride.Should().BeNull();
    }

    [Fact]
    public void Config_BooleanFlags_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert
        config.RejoinAfterLeave.Should().BeFalse("default is not to rejoin after leave");
        config.EnableNameConflictResolution.Should().BeTrue("name conflict resolution enabled by default");
        config.DisableCoordinates.Should().BeFalse("coordinates enabled by default");
        config.ValidateNodeNames.Should().BeFalse("node name validation disabled by default");
        config.MsgpackUseNewTimeFormat.Should().BeFalse("old msgpack time format by default");
    }

    [Fact]
    public void Config_QueueDepths_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert
        config.QueueDepthWarning.Should().Be(128);
        config.MaxQueueDepth.Should().Be(4096);
        config.MinQueueDepth.Should().Be(0, "dynamic queue sizing disabled by default");
    }

    [Fact]
    public void Config_CanSetCustomValues()
    {
        // Arrange
        var config = Config.DefaultConfig();

        // Act - Modify configuration
        config.NodeName = "custom-node";
        config.ProtocolVersion = 5;
        config.BroadcastTimeout = TimeSpan.FromSeconds(10);
        config.ReapInterval = TimeSpan.FromSeconds(30);
        config.EnableNameConflictResolution = false;

        // Assert
        config.NodeName.Should().Be("custom-node");
        config.ProtocolVersion.Should().Be(5);
        config.BroadcastTimeout.Should().Be(TimeSpan.FromSeconds(10));
        config.ReapInterval.Should().Be(TimeSpan.FromSeconds(30));
        config.EnableNameConflictResolution.Should().BeFalse();
    }

    [Fact]
    public void Config_Timeouts_ShouldBePositive()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert - All timeout values should be positive
        config.BroadcastTimeout.Should().BePositive();
        config.LeavePropagateDelay.Should().BePositive();
        config.ReapInterval.Should().BePositive();
        config.RecentIntentTimeout.Should().BePositive();
        config.ReconnectInterval.Should().BePositive();
        config.ReconnectTimeout.Should().BePositive();
        config.QueueCheckInterval.Should().BePositive();
        config.TombstoneTimeout.Should().BePositive();
        config.FlapTimeout.Should().BePositive();
    }

    [Fact]
    public void Config_Buffers_ShouldBePositive()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert - All buffer sizes should be positive
        config.EventBuffer.Should().BePositive();
        config.QueryBuffer.Should().BePositive();
        config.QueueDepthWarning.Should().BePositive();
        config.MaxQueueDepth.Should().BePositive();
        config.QueryTimeoutMult.Should().BePositive();
        config.QueryResponseSizeLimit.Should().BePositive();
        config.QuerySizeLimit.Should().BePositive();
        config.UserEventSizeLimit.Should().BePositive();
    }

    [Fact]
    public void Config_Logger_ShouldNotBeNull()
    {
        // Arrange & Act
        var config = Config.DefaultConfig();

        // Assert
        config.Logger.Should().NotBeNull("Logger should always be set (NullLogger by default)");
    }
}
