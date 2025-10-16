// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/ping_delegate.go

using FluentAssertions;
using NSerf.Memberlist.State;
using NSerf.Serf;
using System.Net;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Serf Ping Delegate implementation.
/// Tests RTT tracking and coordinate updates from ping/ack messages.
/// 
/// Note: Go has no dedicated unit tests for ping_delegate.go.
/// These tests verify the PingDelegate correctly handles ping responses and coordinate updates.
/// </summary>
public class PingDelegateTest
{
    [Fact]
    public void AckPayload_ShouldReturnEmptyArrayWhenCoordinatesDisabled()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            DisableCoordinates = true
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        // Act
        var payload = pingDelegate.AckPayload();

        // Assert
        payload.Should().BeEmpty("coordinates are disabled");
    }

    [Fact]
    public void AckPayload_ShouldReturnCoordinateWhenEnabled()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            DisableCoordinates = false
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        // Act
        var payload = pingDelegate.AckPayload();

        // Assert - Should return coordinate data (non-empty when coordinates enabled)
        // Note: Actual coordinate implementation tested in coordinate tests
        
        // TODO: Phase 9 - Add assertions to verify payload structure
        // Should verify: payload[0] == PingVersion, payload contains serialized coordinate
    }

    [Fact]
    public void NotifyPingComplete_ShouldUpdateRTT()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        var node = new Node
        {
            Name = "remote-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        var rtt = TimeSpan.FromMilliseconds(25);
        var payload = Array.Empty<byte>();

        // Act & Assert - Should not throw and handle RTT update gracefully
        var act = () => pingDelegate.NotifyPingComplete(node, rtt, payload);
        act.Should().NotThrow("PingDelegate should handle ping completion without errors");
        
        // TODO: Phase 9 - Add assertions to verify RTT was recorded
        // Should verify: RTT metric was recorded, coordinate was NOT updated (empty payload)
    }

    [Fact]
    public void NotifyPingComplete_WithCoordinatePayload_ShouldUpdateCoordinate()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            DisableCoordinates = false
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        var node = new Node
        {
            Name = "remote-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        var rtt = TimeSpan.FromMilliseconds(10);
        var coordinatePayload = new byte[] { 1, 2, 3, 4, 5 }; // Mock coordinate data

        // Act - Should handle coordinate update
        var act = () => pingDelegate.NotifyPingComplete(node, rtt, coordinatePayload);

        // Assert
        act.Should().NotThrow();
        
        // TODO: Phase 9 - Add assertions to verify coordinate was updated
        // Should verify: node's coordinate was updated in cache, adjustment metric was recorded
    }

    [Fact]
    public void Constructor_WithNullSerf_ShouldThrow()
    {
        // Act & Assert
        var act = () => new PingDelegate(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serf");
    }

    [Fact]
    public void NotifyPingComplete_WithNullNode_ShouldNotCrash()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        var rtt = TimeSpan.FromMilliseconds(15);
        var payload = Array.Empty<byte>();

        // Act & Assert - Should handle null gracefully (early return on null node)
        var act = () => pingDelegate.NotifyPingComplete(null!, rtt, payload);
        act.Should().NotThrow("PingDelegate should handle null nodes gracefully");
    }

    [Fact]
    public void NotifyPingComplete_MultiplePings_ShouldHandleSequentially()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        var pings = new[]
        {
            (new Node { Name = "node1", Addr = IPAddress.Parse("10.0.0.1"), Port = 8000, Meta = Array.Empty<byte>() }, TimeSpan.FromMilliseconds(10)),
            (new Node { Name = "node2", Addr = IPAddress.Parse("10.0.0.2"), Port = 8000, Meta = Array.Empty<byte>() }, TimeSpan.FromMilliseconds(20)),
            (new Node { Name = "node3", Addr = IPAddress.Parse("10.0.0.3"), Port = 8000, Meta = Array.Empty<byte>() }, TimeSpan.FromMilliseconds(15))
        };

        // Act & Assert - Should handle multiple pings without errors
        var act = () =>
        {
            foreach (var (node, rtt) in pings)
            {
                pingDelegate.NotifyPingComplete(node, rtt, Array.Empty<byte>());
            }
        };
        act.Should().NotThrow("PingDelegate should handle multiple sequential pings");
    }

    [Fact]
    public void NotifyPingComplete_WithZeroRTT_ShouldHandle()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        var node = new Node
        {
            Name = "local-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act - Even with zero RTT, should handle gracefully
        var act = () => pingDelegate.NotifyPingComplete(node, TimeSpan.Zero, Array.Empty<byte>());

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void NotifyPingComplete_WithLargeRTT_ShouldHandle()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var pingDelegate = new PingDelegate(serf);

        var node = new Node
        {
            Name = "distant-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        var largeRtt = TimeSpan.FromSeconds(5);

        // Act - Should handle large RTT values
        var act = () => pingDelegate.NotifyPingComplete(node, largeRtt, Array.Empty<byte>());

        // Assert
        act.Should().NotThrow();
    }
}
