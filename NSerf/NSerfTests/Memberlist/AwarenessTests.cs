// Ported from: github.com/hashicorp/memberlist/awareness.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;

namespace NSerfTests.Memberlist;

public class AwarenessTests
{
    [Fact]
    public void NewAwareness_ShouldInitializeWithZeroScore()
    {
        // Arrange & Act
        var awareness = new Awareness(maxMultiplier: 8);
        
        // Assert
        awareness.GetHealthScore().Should().Be(0, "new awareness should start perfectly healthy");
    }
    
    [Fact]
    public void ApplyDelta_PositiveDelta_ShouldIncreaseScore()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        
        // Act
        awareness.ApplyDelta(3);
        
        // Assert
        awareness.GetHealthScore().Should().Be(3);
    }
    
    [Fact]
    public void ApplyDelta_NegativeDelta_ShouldDecreaseScore()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        awareness.ApplyDelta(5);
        
        // Act
        awareness.ApplyDelta(-2);
        
        // Assert
        awareness.GetHealthScore().Should().Be(3);
    }
    
    [Fact]
    public void ApplyDelta_BelowZero_ShouldClampToZero()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        awareness.ApplyDelta(2);
        
        // Act
        awareness.ApplyDelta(-10);
        
        // Assert
        awareness.GetHealthScore().Should().Be(0, "score should not go below zero");
    }
    
    [Fact]
    public void ApplyDelta_AboveMax_ShouldClampToMaxMinusOne()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        
        // Act
        awareness.ApplyDelta(20);
        
        // Assert
        awareness.GetHealthScore().Should().Be(7, "score should be clamped to max-1 (8-1=7)");
    }
    
    [Fact]
    public void ScaleTimeout_WithZeroScore_ShouldReturnOriginalTimeout()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        var timeout = TimeSpan.FromSeconds(1);
        
        // Act
        var scaled = awareness.ScaleTimeout(timeout);
        
        // Assert
        scaled.Should().Be(timeout, "zero score should not scale timeout");
    }
    
    [Fact]
    public void ScaleTimeout_WithPositiveScore_ShouldIncreaseTimeout()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        awareness.ApplyDelta(3); // Score = 3
        var timeout = TimeSpan.FromSeconds(1);
        
        // Act
        var scaled = awareness.ScaleTimeout(timeout);
        
        // Assert
        // Formula: timeout * (score + 1) = 1s * (3 + 1) = 4s
        scaled.Should().Be(TimeSpan.FromSeconds(4));
    }
    
    [Fact]
    public void ScaleTimeout_WithMaxScore_ShouldScaleByMax()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        awareness.ApplyDelta(100); // Will be clamped to 7
        var timeout = TimeSpan.FromSeconds(1);
        
        // Act
        var scaled = awareness.ScaleTimeout(timeout);
        
        // Assert
        // Formula: timeout * (score + 1) = 1s * (7 + 1) = 8s
        scaled.Should().Be(TimeSpan.FromSeconds(8));
    }
    
    [Fact]
    public async Task GetHealthScore_ShouldBeThreadSafe()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        var tasks = new List<Task>();
        
        // Act - Multiple threads incrementing and reading
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => awareness.ApplyDelta(1)));
        }
        
        await Task.WhenAll(tasks);
        
        // Assert
        var score = awareness.GetHealthScore();
        score.Should().BeLessOrEqualTo(7, "max score is 7");
        score.Should().BeGreaterThan(0, "some deltas should have been applied");
    }
    
    [Fact]
    public void ApplyDelta_MultipleChanges_ShouldAccumulate()
    {
        // Arrange
        var awareness = new Awareness(maxMultiplier: 8);
        
        // Act
        awareness.ApplyDelta(2);
        awareness.ApplyDelta(3);
        awareness.ApplyDelta(-1);
        
        // Assert
        awareness.GetHealthScore().Should().Be(4, "2 + 3 - 1 = 4");
    }
}
