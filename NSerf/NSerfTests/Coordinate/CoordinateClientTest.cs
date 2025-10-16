// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/coordinate/client_test.go

using NSerf.Coordinate;

namespace NSerfTests.Coordinate;

/// <summary>
/// Tests for CoordinateClient.
/// Accurately ported from Go's client_test.go
/// </summary>
public class CoordinateClientTest
{
    private const double FloatTolerance = 1.0e-6;

    [Fact]
    public void NewClient_ShouldValidateDimensionality()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 0;

        // Act & Assert - should throw for zero dimensionality
        Action act = () => new CoordinateClient(config);
        act.Should().Throw<ArgumentException>().WithMessage("*dimensionality must be >0*");

        // Should succeed with valid dimensionality
        config.Dimensionality = 7;
        act = () => new CoordinateClient(config);
        act.Should().NotThrow();
    }

    [Fact]
    public void NewClient_ShouldStartAtOrigin()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 7;

        // Act
        var client = new CoordinateClient(config);
        var coord = client.GetCoordinate();
        var origin = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        // Assert - fresh client should be at origin
        coord.Should().BeEquivalentTo(origin);
    }

    [Fact]
    public void Update_ShouldMoveCoordinateBasedOnRTT()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        // Verify starting at origin
        var c = client.GetCoordinate();
        c.Vec.Should().BeEquivalentTo(new[] { 0.0, 0.0, 0.0 }, options => options.WithStrictOrdering());

        // Place a node right above the client
        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec[2] = 0.001;

        // Observe an RTT longer than expected given the distance
        var rtt = TimeSpan.FromSeconds(2.0 * other.Vec[2]);

        // Act
        c = client.Update("node", other, rtt);

        // Assert - client should have scooted down to get away from it
        c.Vec[2].Should().BeLessThan(0.0, "client z coordinate should be < 0.0");
    }

    [Fact]
    public void SetCoordinate_ShouldOverrideClientPosition()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        // Update to move from origin
        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec[2] = 0.001;
        var rtt = TimeSpan.FromSeconds(2.0 * other.Vec[2]);
        var c = client.Update("node", other, rtt);

        // Act - set to known state
        c.Vec[2] = 99.0;
        client.SetCoordinate(c);

        // Assert
        var coord = client.GetCoordinate();
        coord.Vec[2].Should().BeApproximately(99.0, FloatTolerance);
    }

    [Fact]
    public void Update_ShouldValidateRTTRange()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);
        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        // Act & Assert - negative RTT should throw
        Action act = () => client.Update("node", other, TimeSpan.FromSeconds(-1));
        act.Should().Throw<ArgumentException>().WithMessage("*round trip time not in valid range*");

        // RTT > 10 seconds should throw
        act = () => client.Update("node", other, TimeSpan.FromSeconds(11));
        act.Should().Throw<ArgumentException>().WithMessage("*round trip time not in valid range*");

        // Valid RTT should not throw
        act = () => client.Update("node", other, TimeSpan.FromMilliseconds(50));
        act.Should().NotThrow();
    }

    [Fact]
    public void Update_ShouldValidateCoordinateCompatibility()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        // Create incompatible coordinate
        var otherConfig = CoordinateConfig.DefaultConfig();
        otherConfig.Dimensionality = 5;
        var other = NSerf.Coordinate.Coordinate.NewCoordinate(otherConfig);

        // Act & Assert
        Action act = () => client.Update("node", other, TimeSpan.FromMilliseconds(50));
        act.Should().Throw<ArgumentException>().WithMessage("*dimensions aren't compatible*");
    }

    [Fact]
    public void Update_ShouldRejectInvalidCoordinates()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec[0] = double.NaN;

        // Act & Assert
        Action act = () => client.Update("node", other, TimeSpan.FromMilliseconds(50));
        act.Should().Throw<ArgumentException>().WithMessage("*coordinate is invalid*");
    }

    [Fact]
    public void SetCoordinate_ShouldRejectInvalidCoordinates()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        var client = new CoordinateClient(config);

        var bad = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        bad.Vec[0] = double.PositiveInfinity;

        // Act & Assert
        Action act = () => client.SetCoordinate(bad);
        act.Should().Throw<ArgumentException>().WithMessage("*coordinate is invalid*");
    }

    [Fact]
    public void ForgetNode_ShouldRemoveLatencyFilter()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec[2] = 0.001;

        // Update creates latency filter entry
        client.Update("node1", other, TimeSpan.FromMilliseconds(50));

        // Act - forget the node
        client.ForgetNode("node1");

        // Assert - should not throw (internal state cleared)
        Action act = () => client.ForgetNode("node1");
        act.Should().NotThrow();
    }

    [Fact]
    public void Stats_ShouldTrackResets()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        // Assert - should start with zero resets
        client.Stats().Resets.Should().Be(0);

        // This scenario would potentially cause resets in real usage
        // but we'll just verify the stats structure works
        var stats = client.Stats();
        stats.Should().NotBeNull();
    }

    [Fact]
    public void DistanceTo_ShouldCalculateEstimatedRTT()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        config.HeightMin = 0;

        var client = new CoordinateClient(config);

        // Set client to known position
        var coord = client.GetCoordinate();
        coord.Vec = new[] { 0.5, 1.0, 1.5 };
        client.SetCoordinate(coord);

        // Create other coordinate
        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec = new[] { 1.5, 2.0, 3.0 };

        // Act
        var distance = client.DistanceTo(other);

        // Assert - should calculate distance correctly
        distance.TotalSeconds.Should().BeGreaterThan(0);
        
        // Verify it matches direct coordinate calculation
        var directDistance = coord.DistanceTo(other);
        distance.TotalSeconds.Should().BeApproximately(directDistance.TotalSeconds, FloatTolerance);
    }

    [Fact]
    public void Update_MultipleTimes_ShouldConverge()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec[2] = 0.002;

        // Act - multiple updates with consistent RTT
        var rtt = TimeSpan.FromSeconds(2.0 * other.Vec[2]);
        for (int i = 0; i < 10; i++)
        {
            client.Update($"node{i % 2}", other, rtt);
        }

        // Assert - coordinate should have moved
        var c = client.GetCoordinate();
        c.Vec.Should().NotBeEquivalentTo(new[] { 0.0, 0.0, 0.0 });
        
        // Error should have decreased from initial max
        c.Error.Should().BeLessThan(config.VivaldiErrorMax);
    }

    [Fact]
    public void Update_WithInvalidRTTValues_ShouldRejectAndNotModifyCoordinate()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        var other = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        other.Vec[2] = 0.001;
        var initialDist = client.DistanceTo(other);

        // Test various invalid RTT values
        var invalidRTTs = new[]
        {
            TimeSpan.FromSeconds(long.MaxValue / 1e9), // Too large
            TimeSpan.FromSeconds(-35), // Negative
            TimeSpan.FromSeconds(11) // > 10 second max
        };

        foreach (var invalidRtt in invalidRTTs)
        {
            // Act & Assert
            Action act = () => client.Update("node", other, invalidRtt);
            act.Should().Throw<ArgumentException>()
                .WithMessage("*round trip time not in valid range*");

            // Distance should remain unchanged after failed update
            var currentDist = client.DistanceTo(other);
            currentDist.Should().Be(initialDist, "distance should not change after failed update");
        }
    }

    [Fact]
    public void LatencyFilter_ShouldCalculateMovingMedian()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.LatencyFilterSize = 3;
        var client = new CoordinateClient(config);

        // Use reflection to access private latencyFilter method
        var filterMethod = typeof(CoordinateClient).GetMethod("LatencyFilter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // Act & Assert - Build up samples and verify median
        // First sample: [0.201] → median = 0.201
        var result1 = (double)filterMethod!.Invoke(client, new object[] { "alice", 0.201 })!;
        result1.Should().BeApproximately(0.201, FloatTolerance);

        // Second sample: [0.201, 0.200] → median = 0.201 (middle of sorted)
        var result2 = (double)filterMethod.Invoke(client, new object[] { "alice", 0.200 })!;
        result2.Should().BeApproximately(0.201, FloatTolerance);

        // Third sample: [0.201, 0.200, 0.207] → median = 0.201 (middle element)
        var result3 = (double)filterMethod.Invoke(client, new object[] { "alice", 0.207 })!;
        result3.Should().BeApproximately(0.201, FloatTolerance);

        // Fourth sample - glitch: [0.200, 0.207, 1.9] → median = 0.207 (filters out glitch!)
        var result4 = (double)filterMethod.Invoke(client, new object[] { "alice", 1.9 })!;
        result4.Should().BeApproximately(0.207, FloatTolerance, "glitch should be filtered by median");

        // Fifth sample: [0.207, 1.9, 0.203] → median = 0.207
        var result5 = (double)filterMethod.Invoke(client, new object[] { "alice", 0.203 })!;
        result5.Should().BeApproximately(0.207, FloatTolerance);

        // Sixth sample: [1.9, 0.203, 0.199] → median = 0.203
        var result6 = (double)filterMethod.Invoke(client, new object[] { "alice", 0.199 })!;
        result6.Should().BeApproximately(0.203, FloatTolerance);

        // Seventh sample: [0.203, 0.199, 0.211] → median = 0.203
        var result7 = (double)filterMethod.Invoke(client, new object[] { "alice", 0.211 })!;
        result7.Should().BeApproximately(0.203, FloatTolerance);

        // Different node should have separate filter
        var resultBob = (double)filterMethod.Invoke(client, new object[] { "bob", 0.310 })!;
        resultBob.Should().BeApproximately(0.310, FloatTolerance, "bob should have independent filter");

        // ForgetNode should clear state
        client.ForgetNode("alice");
        var resultAliceAfter = (double)filterMethod.Invoke(client, new object[] { "alice", 0.888 })!;
        resultAliceAfter.Should().BeApproximately(0.888, FloatTolerance, "alice filter should be cleared");
    }

    [Fact]
    public void NaNDefense_ShouldBlockInvalidCoordinatesAndResetOnPoison()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        var client = new CoordinateClient(config);

        // Block bad coordinate from Update
        var badCoord = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        badCoord.Vec[0] = double.NaN;
        badCoord.IsValid().Should().BeFalse();

        var rtt = TimeSpan.FromMilliseconds(250);
        Action act = () => client.Update("node", badCoord, rtt);
        act.Should().Throw<ArgumentException>().WithMessage("*coordinate is invalid*");
        
        // Client's coordinate should still be valid
        client.GetCoordinate().IsValid().Should().BeTrue();

        // Block setting invalid coordinate directly
        act = () => client.SetCoordinate(badCoord);
        act.Should().Throw<ArgumentException>().WithMessage("*coordinate is invalid*");
        client.GetCoordinate().IsValid().Should().BeTrue();

        // Block incompatible coordinate in Update
        var incompatible = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        incompatible.Vec = new double[2 * incompatible.Vec.Length];
        act = () => client.Update("node", incompatible, rtt);
        act.Should().Throw<ArgumentException>().WithMessage("*dimensions aren't compatible*");
        client.GetCoordinate().IsValid().Should().BeTrue();

        // Block setting incompatible coordinate directly
        act = () => client.SetCoordinate(incompatible);
        act.Should().Throw<ArgumentException>().WithMessage("*dimensions aren't compatible*");
        client.GetCoordinate().IsValid().Should().BeTrue();

        // Poison internal state using reflection (since coord is private)
        var coordField = typeof(CoordinateClient).GetField("_coord", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var internalCoord = (NSerf.Coordinate.Coordinate)coordField!.GetValue(client)!;
        internalCoord.Vec[0] = double.NaN;

        // Now update with valid coordinate - should detect invalid state and reset
        var validCoord = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var result = client.Update("node", validCoord, rtt);
        
        // Result should be valid (reset to new coordinate at origin)
        result.IsValid().Should().BeTrue();
        
        // Stats should show one reset
        client.Stats().Resets.Should().Be(1);
    }
}
