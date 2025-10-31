// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/ping_delegate.go

using FluentAssertions;
using MessagePack;
using NSerf.Coordinate;
using NSerf.Memberlist.Configuration;
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
        payload.Should().NotBeEmpty("coordinates are enabled");
        
        // Verify payload structure: [version byte][msgpack coordinate]
        payload[0].Should().Be(PingDelegate.PingVersion, "first byte should be ping version");
        
        // Deserialize and verify we got a valid coordinate
        var coordinateBytes = payload[1..];
        coordinateBytes.Should().NotBeEmpty("coordinate data should be present");
        
        var act = () => MessagePackSerializer.Deserialize<NSerf.Coordinate.Coordinate>(coordinateBytes);
        act.Should().NotThrow("payload should contain valid MessagePack coordinate");
        
        var coordinate = act();
        coordinate.Should().NotBeNull("deserialized coordinate should not be null");
        coordinate.Vec.Should().NotBeNull("coordinate vector should be initialized");
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
        
        // Verify coordinate was NOT updated (empty payload)
        var cachedCoordinate = serf.GetCachedCoordinate(node.Name);
        cachedCoordinate.Should().BeNull("coordinate should not be cached when payload is empty");
    }

    [Fact]
    public async Task NotifyPingComplete_WithCoordinatePayload_ShouldUpdateCoordinate()
    {
        // Arrange
        var config = new NSerf.Serf.Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            DisableCoordinates = false,
            MemberlistConfig = MemberlistConfig.DefaultLANConfig()
        };
        config.MemberlistConfig.BindAddr = "127.0.0.1";
        config.MemberlistConfig.BindPort = 0; // Random port
        
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        var pingDelegate = new PingDelegate(serf);
        
        try
        {
            var node = new Node
            {
                Name = "remote-node",
                Addr = IPAddress.Parse("127.0.0.1"),
                Port = 8000,
                Meta = Array.Empty<byte>()
            };

            var rtt = TimeSpan.FromMilliseconds(10);
            
            // Create proper coordinate payload: [PingVersion byte][MessagePack serialized coordinate]
            // Note: Default coordinate dimensionality is 8
            var remoteCoordinate = new NSerf.Coordinate.Coordinate
            {
                Vec = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 },
                Error = 1.5,
                Adjustment = 0.0,
                Height = 0.001
            };
            var coordinateBytes = MessagePackSerializer.Serialize(remoteCoordinate);
            var coordinatePayload = new byte[1 + coordinateBytes.Length];
            coordinatePayload[0] = PingDelegate.PingVersion;
            Array.Copy(coordinateBytes, 0, coordinatePayload, 1, coordinateBytes.Length);

            // Act - Should handle coordinate update
            pingDelegate.NotifyPingComplete(node, rtt, coordinatePayload);
            
            // Verify coordinate was updated in cache
            var cachedCoordinate = serf.GetCachedCoordinate(node.Name);
            cachedCoordinate.Should().NotBeNull("coordinate should be cached after ping complete");
            cachedCoordinate!.Vec.Should().NotBeNull("cached coordinate should have a vector");
            cachedCoordinate.Vec.Length.Should().Be(8, "coordinate should have 8 dimensions");
        }
        finally
        {
            await serf.ShutdownAsync();
            await serf.DisposeAsync();
        }
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
