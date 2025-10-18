// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf_test.go (coordinate tests)

using Xunit;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using FluentAssertions;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for network coordinate (Vivaldi) integration in Serf.
/// Based on serf_test.go coordinate tests.
/// </summary>
public class CoordinateTest
{
    /// <summary>
    /// Test: Coordinates should be disabled when DisableCoordinates=true
    /// </summary>
    [Fact]
    public async Task Serf_Coordinates_Disabled_ShouldReturnEmptyAckPayload()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            DisableCoordinates = true,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Get the ping delegate and check ack payload
        var pingDelegate = new PingDelegate(serf);
        var ackPayload = pingDelegate.AckPayload();

        // Assert
        ackPayload.Should().BeEmpty("coordinates are disabled, so ack payload should be empty");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Test: Coordinates should be enabled by default and return non-empty ack payload
    /// </summary>
    [Fact]
    public async Task Serf_Coordinates_Enabled_ShouldReturnAckPayloadWithVersionAndCoordinate()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            DisableCoordinates = false, // Explicitly enabled
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Get the ping delegate and check ack payload
        var pingDelegate = new PingDelegate(serf);
        var ackPayload = pingDelegate.AckPayload();

        // Assert
        ackPayload.Should().NotBeEmpty("coordinates are enabled, so ack payload should contain version + coordinate");
        ackPayload[0].Should().Be(PingDelegate.PingVersion, "first byte should be ping version");
        ackPayload.Length.Should().BeGreaterThan(1, "payload should contain coordinate data after version byte");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Test: GetCoordinate should return a valid coordinate
    /// </summary>
    [Fact]
    public async Task Serf_GetCoordinate_ShouldReturnValidCoordinate()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            DisableCoordinates = false,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act
        var coordinate = serf.GetCoordinate();

        // Assert
        coordinate.Should().NotBeNull("coordinate should be initialized");
        coordinate.Vec.Should().NotBeNull("coordinate vector should be initialized");
        coordinate.Vec.Length.Should().BeGreaterThan(0, "coordinate should have dimensions");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Test: UpdateCoordinate should update the local coordinate based on RTT
    /// </summary>
    [Fact]
    public async Task Serf_UpdateCoordinate_ShouldUpdateLocalCoordinate()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            DisableCoordinates = false,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Get initial coordinate
        var initialCoord = serf.GetCoordinate();
        var initialVec = (double[])initialCoord.Vec.Clone();

        // Create a remote coordinate (simulating another node)
        var remoteCoord = new NSerf.Coordinate.Coordinate
        {
            Vec = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 },
            Error = 0.5,
            Adjustment = 0.0,
            Height = 0.0
        };

        // Act - Update coordinate with a known RTT
        serf.UpdateCoordinate("remote-node", remoteCoord, TimeSpan.FromMilliseconds(50));

        // Allow some time for async update (if any)
        await Task.Delay(50);

        // Get updated coordinate
        var updatedCoord = serf.GetCoordinate();

        // Assert - The coordinate should have changed after the update
        // Note: We can't predict exact values, but we can verify the update happened
        updatedCoord.Should().NotBeNull("updated coordinate should exist");
        
        // At least one dimension should have changed after the update
        bool coordinateChanged = false;
        for (int i = 0; i < Math.Min(initialVec.Length, updatedCoord.Vec.Length); i++)
        {
            if (Math.Abs(initialVec[i] - updatedCoord.Vec[i]) > 1e-10)
            {
                coordinateChanged = true;
                break;
            }
        }
        
        coordinateChanged.Should().BeTrue("coordinate should change after update with RTT observation");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Test: Coordinates should be disabled when DisableCoordinates=true (no updates)
    /// </summary>
    [Fact]
    public async Task Serf_Coordinates_Disabled_ShouldNotUpdateCoordinate()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            DisableCoordinates = true,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Get initial coordinate (should be default/zero)
        var initialCoord = serf.GetCoordinate();

        // Create a remote coordinate
        var remoteCoord = new NSerf.Coordinate.Coordinate
        {
            Vec = new double[] { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8 },
            Error = 0.5,
            Adjustment = 0.0,
            Height = 0.0
        };

        // Act - Attempt to update coordinate (should be ignored)
        serf.UpdateCoordinate("remote-node", remoteCoord, TimeSpan.FromMilliseconds(50));

        await Task.Delay(50);

        // Get coordinate again
        var finalCoord = serf.GetCoordinate();

        // Assert - Coordinate should not have changed (updates ignored when disabled)
        finalCoord.Vec.Should().Equal(initialCoord.Vec, "coordinate should not change when coordinates are disabled");

        await serf.ShutdownAsync();
    }
}
