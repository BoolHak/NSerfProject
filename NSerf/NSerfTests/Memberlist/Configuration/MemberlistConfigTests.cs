// Ported from: github.com/hashicorp/memberlist/config.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Messages;
using IPNetwork = NSerf.Memberlist.Configuration.IPNetwork;

namespace NSerfTests.Memberlist.Configuration;

public class MemberlistConfigTests
{
    [Fact]
    public void DefaultLANConfig_ShouldHaveSaneDefaults()
    {
        // Act
        var config = MemberlistConfig.DefaultLANConfig();

        // Assert - Basic settings
        config.Name.Should().NotBeNullOrEmpty();
        config.BindAddr.Should().Be("0.0.0.0");
        config.BindPort.Should().Be(7946);
        config.AdvertisePort.Should().Be(7946);
        config.ProtocolVersion.Should().Be(MessageConstants.ProtocolVersion2Compatible);

        // Assert - Timeouts
        config.TCPTimeout.Should().Be(TimeSpan.FromSeconds(10));
        config.ProbeTimeout.Should().Be(TimeSpan.FromMilliseconds(500));
        config.ProbeInterval.Should().Be(TimeSpan.FromSeconds(1));
        config.PushPullInterval.Should().Be(TimeSpan.FromSeconds(30));

        // Assert - Multipliers
        config.IndirectChecks.Should().Be(3);
        config.RetransmitMult.Should().Be(4);
        config.SuspicionMult.Should().Be(4);
        config.SuspicionMaxTimeoutMult.Should().Be(6);
        config.AwarenessMaxMultiplier.Should().Be(8);

        // Assert - Gossip settings
        config.GossipNodes.Should().Be(3);
        config.GossipInterval.Should().Be(TimeSpan.FromMilliseconds(200));
        config.GossipToTheDeadTime.Should().Be(TimeSpan.FromSeconds(30));
        config.GossipVerifyIncoming.Should().BeTrue();
        config.GossipVerifyOutgoing.Should().BeTrue();

        // Assert - Other settings
        config.EnableCompression.Should().BeTrue();
        config.DisableTcpPings.Should().BeFalse();
        config.HandoffQueueDepth.Should().Be(1024);
        config.UDPBufferSize.Should().Be(1400);
    }

    [Fact]
    public void DefaultWANConfig_ShouldBeOptimizedForWAN()
    {
        // Act
        var config = MemberlistConfig.DefaultWANConfig();

        // Assert - WAN-specific timeouts (longer)
        config.TCPTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.ProbeTimeout.Should().Be(TimeSpan.FromSeconds(3));
        config.ProbeInterval.Should().Be(TimeSpan.FromSeconds(5));
        config.PushPullInterval.Should().Be(TimeSpan.FromSeconds(60));

        // Assert - WAN-specific gossip (more nodes, less frequent)
        config.GossipNodes.Should().Be(4);
        config.GossipInterval.Should().Be(TimeSpan.FromMilliseconds(500));
        config.GossipToTheDeadTime.Should().Be(TimeSpan.FromSeconds(60));

        // Assert - Higher suspicion mult for WAN
        config.SuspicionMult.Should().Be(6);
    }

    [Fact]
    public void DefaultLocalConfig_ShouldBeOptimizedForLoopback()
    {
        // Act
        var config = MemberlistConfig.DefaultLocalConfig();

        // Assert - Local-specific (faster, lower overhead)
        config.TCPTimeout.Should().Be(TimeSpan.FromSeconds(1));
        config.ProbeTimeout.Should().Be(TimeSpan.FromMilliseconds(200));
        config.ProbeInterval.Should().Be(TimeSpan.FromSeconds(1));
        config.PushPullInterval.Should().Be(TimeSpan.FromSeconds(15));

        // Assert - Lower multipliers for local
        config.IndirectChecks.Should().Be(1);
        config.RetransmitMult.Should().Be(2);
        config.SuspicionMult.Should().Be(3);

        // Assert - Faster gossip for local
        config.GossipInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        config.GossipToTheDeadTime.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Config_CustomName_ShouldBeSet()
    {
        // Arrange & Act
        var config = new MemberlistConfig
        {
            Name = "custom-node"
        };

        // Assert
        config.Name.Should().Be("custom-node");
    }

    [Fact]
    public void Config_CustomBindAddress_ShouldBeSet()
    {
        // Arrange & Act
        var config = new MemberlistConfig
        {
            BindAddr = "192.168.1.100",
            BindPort = 8080
        };

        // Assert
        config.BindAddr.Should().Be("192.168.1.100");
        config.BindPort.Should().Be(8080);
    }

    [Fact]
    public void EncryptionEnabled_WithoutKeyring_ShouldReturnFalse()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLANConfig();

        // Act & Assert
        config.EncryptionEnabled().Should().BeFalse();
    }

    [Fact]
    public void IPMustBeChecked_WithNoCIDRs_ShouldReturnFalse()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLANConfig();

        // Act & Assert
        config.IPMustBeChecked().Should().BeFalse();
    }

    [Fact]
    public void IPMustBeChecked_WithCIDRs_ShouldReturnTrue()
    {
        // Arrange
        var config = new MemberlistConfig
        {
            CIDRsAllowed =
            [
                IPNetwork.Parse("192.168.0.0/16")
            ]
        };

        // Act & Assert
        config.IPMustBeChecked().Should().BeTrue();
    }

    [Fact]
    public void IPAllowed_WithNoCIDRs_ShouldAllowAny()
    {
        // Arrange
        var config = MemberlistConfig.DefaultLANConfig();
        var ip = IPAddress.Parse("10.0.0.1");

        // Act
        var result = config.IPAllowed(ip);

        // Assert
        result.Should().BeNull("no CIDRs means allow all");
    }

    [Fact]
    public void IPAllowed_WithMatchingCIDR_ShouldAllow()
    {
        // Arrange
        var config = new MemberlistConfig
        {
            CIDRsAllowed =
            [
                IPNetwork.Parse("192.168.0.0/16")
            ]
        };
        var ip = IPAddress.Parse("192.168.1.100");

        // Act
        var result = config.IPAllowed(ip);

        // Assert
        result.Should().BeNull("IP is in allowed CIDR");
    }

    [Fact]
    public void IPAllowed_WithNonMatchingCIDR_ShouldDeny()
    {
        // Arrange
        var config = new MemberlistConfig
        {
            CIDRsAllowed =
            [
                IPNetwork.Parse("192.168.0.0/16")
            ]
        };
        var ip = IPAddress.Parse("10.0.0.1");

        // Act
        var result = config.IPAllowed(ip);

        // Assert
        result.Should().NotBeNull("IP is not in allowed CIDR");
        result.Should().Contain("10.0.0.1");
        result.Should().Contain("not allowed");
    }
}
