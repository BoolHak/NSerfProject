// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/coordinate/coordinate_test.go

using NSerf.Coordinate;

namespace NSerfTests.Coordinate;

/// <summary>
/// Tests for Coordinate and network coordinate system.
/// Accurately ported from Go's coordinate_test.go
/// </summary>
public class CoordinateTest
{
    private const double FloatTolerance = 1.0e-6;

    [Fact]
    public void NewCoordinate_ShouldHaveCorrectDimensionality()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();

        // Act
        var c = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        // Assert
        c.Vec.Length.Should().Be((int)config.Dimensionality, "dimensionality not set correctly");
    }

    [Fact]
    public void Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var c = NSerf.Coordinate.Coordinate.NewCoordinate(CoordinateConfig.DefaultConfig());
        c.Vec[0] = 1.0;
        c.Vec[1] = 2.0;
        c.Vec[2] = 3.0;
        c.Error = 5.0;
        c.Adjustment = 10.0;
        c.Height = 4.2;

        // Act
        var other = c.Clone();

        // Assert
        c.Should().BeEquivalentTo(other, "coordinate clone didn't make a proper copy");

        // Modify clone
        other.Vec[0] = c.Vec[0] + 0.5;
        c.Should().NotBeEquivalentTo(other, "cloned coordinate is still pointing at its ancestor");
    }

    [Fact]
    public void IsValid_ShouldDetectInvalidComponents()
    {
        // Arrange
        var c = NSerf.Coordinate.Coordinate.NewCoordinate(CoordinateConfig.DefaultConfig());

        // Test all fields for NaN and Infinity
        var fields = new List<Action<double>>
        {
            v => c.Vec[0] = v,
            v => c.Vec[1] = v,
            v => c.Error = v,
            v => c.Adjustment = v,
            v => c.Height = v
        };

        for (int i = 0; i < fields.Count; i++)
        {
            // Should be valid initially
            c.IsValid().Should().BeTrue($"field {i} should be valid");

            // Test NaN
            fields[i](double.NaN);
            c.IsValid().Should().BeFalse($"field {i} should not be valid (NaN)");

            // Reset
            fields[i](0.0);
            c.IsValid().Should().BeTrue($"field {i} should be valid after reset");

            // Test Infinity
            fields[i](double.PositiveInfinity);
            c.IsValid().Should().BeFalse($"field {i} should not be valid (Inf)");

            // Reset
            fields[i](0.0);
            c.IsValid().Should().BeTrue($"field {i} should be valid after reset");
        }
    }

    [Fact]
    public void IsCompatibleWith_ShouldCheckDimensionality()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();

        config.Dimensionality = 3;
        var c1 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var c2 = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        config.Dimensionality = 2;
        var alien = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        // Assert - coordinates should be compatible with themselves
        c1.IsCompatibleWith(c1).Should().BeTrue();
        c2.IsCompatibleWith(c2).Should().BeTrue();
        alien.IsCompatibleWith(alien).Should().BeTrue();

        // Coordinates with same dimensionality should be compatible
        c1.IsCompatibleWith(c2).Should().BeTrue();
        c2.IsCompatibleWith(c1).Should().BeTrue();

        // Different dimensionality should not be compatible
        c1.IsCompatibleWith(alien).Should().BeFalse();
        c2.IsCompatibleWith(alien).Should().BeFalse();
        alien.IsCompatibleWith(c1).Should().BeFalse();
        alien.IsCompatibleWith(c2).Should().BeFalse();
    }

    [Fact]
    public void ApplyForce_ShouldNormalizeAndApplyCorrectly()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        config.HeightMin = 0;

        var origin = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        // Act & Assert - normalize, get direction right, apply force multiplier
        var above = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        above.Vec = new[] { 0.0, 0.0, 2.9 };
        var c = origin.ApplyForce(config, 5.3, above);
        c.Vec.Should().BeEquivalentTo(new[] { 0.0, 0.0, -5.3 }, options => options.WithStrictOrdering());

        // Scoot a point not starting at the origin
        var right = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        right.Vec = new[] { 3.4, 0.0, -5.3 };
        c = c.ApplyForce(config, 2.0, right);
        c.Vec.Should().BeEquivalentTo(new[] { -2.0, 0.0, -5.3 }, options => options.WithStrictOrdering());

        // Points on top of each other should end up one unit away in random direction
        c = origin.ApplyForce(config, 1.0, origin);
        origin.DistanceTo(c).TotalSeconds.Should().BeApproximately(1.0, FloatTolerance);
    }

    [Fact]
    public void ApplyForce_ShouldEnforceHeightMinimum()
    {
        // Arrange - create "above" with HeightMin = 0
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        config.HeightMin = 0;
        
        var above = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        above.Vec = new[] { 0.0, 0.0, 2.9 };

        // Now set HeightMin and create new origin (matches Go test exactly)
        config.HeightMin = 10.0e-6;
        var origin = NSerf.Coordinate.Coordinate.NewCoordinate(config);

        // Act - apply positive force
        var c = origin.ApplyForce(config, 5.3, above);

        // Assert
        c.Vec.Should().BeEquivalentTo(new[] { 0.0, 0.0, -5.3 }, options => options.WithStrictOrdering());
        var expectedHeight = config.HeightMin + 5.3 * config.HeightMin / 2.9;
        c.Height.Should().BeApproximately(expectedHeight, FloatTolerance);

        // Act - apply negative force
        c = origin.ApplyForce(config, -5.3, above);

        // Assert - height minimum should be enforced
        c.Vec.Should().BeEquivalentTo(new[] { 0.0, 0.0, 5.3 }, options => options.WithStrictOrdering());
        c.Height.Should().BeApproximately(config.HeightMin, FloatTolerance);
    }

    [Fact]
    public void ApplyForce_ShouldThrowOnDimensionMismatch()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        var c = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var bad = c.Clone();
        bad.Vec = new double[bad.Vec.Length + 1];

        // Act & Assert
        Action act = () => c.ApplyForce(config, 1.0, bad);
        act.Should().Throw<DimensionalityConflictException>();
    }

    [Fact]
    public void DistanceTo_ShouldCalculateCorrectly()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        config.HeightMin = 0;

        var c1 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var c2 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        c1.Vec = new[] { -0.5, 1.3, 2.4 };
        c2.Vec = new[] { 1.2, -2.3, 3.4 };

        // Assert - distance to self is zero
        c1.DistanceTo(c1).TotalSeconds.Should().BeApproximately(0.0, FloatTolerance);

        // Assert - distance is symmetric
        c1.DistanceTo(c2).TotalSeconds.Should().BeApproximately(c2.DistanceTo(c1).TotalSeconds, FloatTolerance);

        // Assert - actual distance calculation
        c1.DistanceTo(c2).TotalSeconds.Should().BeApproximately(4.104875150354758, FloatTolerance);
    }

    [Fact]
    public void DistanceTo_ShouldHandleAdjustments()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        config.HeightMin = 0;

        var c1 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var c2 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        c1.Vec = new[] { -0.5, 1.3, 2.4 };
        c2.Vec = new[] { 1.2, -2.3, 3.4 };

        const double baseDistance = 4.104875150354758;

        // Negative adjustment factors should be ignored
        c1.Adjustment = -1.0e6;
        c1.DistanceTo(c2).TotalSeconds.Should().BeApproximately(baseDistance, FloatTolerance);

        // Positive adjustment factors should affect distance
        c1.Adjustment = 0.1;
        c2.Adjustment = 0.2;
        c1.DistanceTo(c2).TotalSeconds.Should().BeApproximately(baseDistance + 0.3, FloatTolerance);

        // Heights should affect distance
        c1.Height = 0.7;
        c2.Height = 0.1;
        c1.DistanceTo(c2).TotalSeconds.Should().BeApproximately(baseDistance + 0.3 + 0.8, FloatTolerance);
    }

    [Fact]
    public void DistanceTo_ShouldThrowOnDimensionMismatch()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        var c1 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var bad = c1.Clone();
        bad.Vec = new double[bad.Vec.Length + 1];

        // Act & Assert
        Action act = () => c1.DistanceTo(bad);
        act.Should().Throw<DimensionalityConflictException>();
    }

    [Fact]
    public void DefaultConfig_ShouldHaveReasonableValues()
    {
        // Act
        var config = CoordinateConfig.DefaultConfig();

        // Assert - verify all defaults match Go implementation
        config.Dimensionality.Should().Be(8u);
        config.VivaldiErrorMax.Should().Be(1.5);
        config.VivaldiCE.Should().Be(0.25);
        config.VivaldiCC.Should().Be(0.25);
        config.AdjustmentWindowSize.Should().Be(20u);
        config.HeightMin.Should().Be(10.0e-6);
        config.LatencyFilterSize.Should().Be(3u);
        config.GravityRho.Should().Be(150.0);
    }

    [Fact]
    public void RawDistanceTo_ShouldNotIncludeAdjustments()
    {
        // Arrange
        var config = CoordinateConfig.DefaultConfig();
        config.Dimensionality = 3;
        config.HeightMin = 0;

        var c1 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        var c2 = NSerf.Coordinate.Coordinate.NewCoordinate(config);
        c1.Vec = new[] { -0.5, 1.3, 2.4 };
        c2.Vec = new[] { 1.2, -2.3, 3.4 };

        const double baseDistance = 4.104875150354758;

        // Assert - raw distance should be symmetric
        c1.RawDistanceTo(c1).Should().BeApproximately(0.0, FloatTolerance);
        c1.RawDistanceTo(c2).Should().BeApproximately(c2.RawDistanceTo(c1), FloatTolerance);
        c1.RawDistanceTo(c2).Should().BeApproximately(baseDistance, FloatTolerance);

        // Adjustments should NOT affect raw distance
        c1.Adjustment = 1.0e6;
        c1.RawDistanceTo(c2).Should().BeApproximately(baseDistance, FloatTolerance);

        // Heights should be included in raw distance
        c1.Height = 0.7;
        c2.Height = 0.1;
        c1.RawDistanceTo(c2).Should().BeApproximately(baseDistance + 0.8, FloatTolerance);
    }

}
