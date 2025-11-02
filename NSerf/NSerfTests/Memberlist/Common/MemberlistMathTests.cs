// Ported from: github.com/hashicorp/memberlist/util_test.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Common;

namespace NSerfTests.Memberlist.Common;

public class MemberlistMathTests
{
    [Fact]
    public void RandomOffset_ShouldReturnZero_WhenNIsZero()
    {
        // Act
        var offset = MemberlistMath.RandomOffset(0);
        
        // Assert
        offset.Should().Be(0);
    }
    
    [Fact]
    public void RandomOffset_ShouldReturnDifferentValues()
    {
        // Arrange
        var values = new HashSet<int>();
        
        // Act - generate 100 random offsets
        for (int i = 0; i < 100; i++)
        {
            var offset = MemberlistMath.RandomOffset(1 << 30); // Large range
            values.Add(offset);
        }
        
        // Assert - should have many different values (allow for some collisions)
        values.Count.Should().BeGreaterThan(90, "random values should not collide frequently");
    }
    
    [Theory]
    [InlineData(5, 1000)]    // 1000ms
    [InlineData(10, 1000)]   // 1000ms
    [InlineData(50, 1698)]   // 1698ms
    [InlineData(100, 2000)]  // 2000ms
    [InlineData(500, 2698)]  // 2698ms
    [InlineData(1000, 3000)] // 3000ms
    public void SuspicionTimeout_ShouldCalculateCorrectly(int nodes, int expectedMs)
    {
        // Arrange
        const int suspicionMult = 3;
        var interval = TimeSpan.FromSeconds(1);
        
        // Act
        var timeout = MemberlistMath.SuspicionTimeout(suspicionMult, nodes, interval);
        var timeoutDividedBy3 = timeout / 3;
        
        // Assert
        timeoutDividedBy3.TotalMilliseconds.Should().BeApproximately(expectedMs, 1, 
            $"for {nodes} nodes");
    }
    
    [Theory]
    [InlineData(0, 0)]  // Special case
    [InlineData(1, 3)]  // log10(2) = 0.301, ceil = 1, 3 * 1 = 3
    [InlineData(99, 6)] // log10(100) = 2, ceil = 2, 3 * 2 = 6
    public void RetransmitLimit_ShouldCalculateCorrectly(int nodes, int expected)
    {
        // Arrange
        const int retransmitMult = 3;
        
        // Act
        var limit = MemberlistMath.RetransmitLimit(retransmitMult, nodes);
        
        // Assert
        limit.Should().Be(expected, $"for {nodes} nodes");
    }
    
    [Fact]
    public void PushPullScale_ShouldNotScale_WhenNodesBelowThreshold()
    {
        // Arrange
        var interval = TimeSpan.FromSeconds(1);
        
        // Act & Assert - should not scale up to 32 nodes
        for (int i = 0; i <= 32; i++)
        {
            var scaled = MemberlistMath.PushPullScale(interval, i);
            scaled.Should().Be(interval, $"for {i} nodes (below threshold)");
        }
    }
    
    [Fact]
    public void PushPullScale_ShouldDoubleInterval_For33To64Nodes()
    {
        // Arrange
        var interval = TimeSpan.FromSeconds(1);
        var expected = TimeSpan.FromSeconds(2);
        
        // Act & Assert - should double for 33-64 nodes
        for (int i = 33; i <= 64; i++)
        {
            var scaled = MemberlistMath.PushPullScale(interval, i);
            scaled.Should().Be(expected, $"for {i} nodes (should double)");
        }
    }
    
    [Fact]
    public void PushPullScale_ShouldTripleInterval_For65To128Nodes()
    {
        // Arrange
        var interval = TimeSpan.FromSeconds(1);
        var expected = TimeSpan.FromSeconds(3);
        
        // Act & Assert - should triple for 65-128 nodes
        for (int i = 65; i <= 128; i++)
        {
            var scaled = MemberlistMath.PushPullScale(interval, i);
            scaled.Should().Be(expected, $"for {i} nodes (should triple)");
        }
    }
}
