// Ported from: github.com/hashicorp/memberlist/suspicion.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist;

namespace NSerfTests.Memberlist;

public class SuspicionTests
{
    [Fact]
    public async Task NewSuspicion_WithNoConfirmationsNeeded_ShouldUseMinTimeout()
    {
        // Arrange
        var timeoutCalled = false;
        var min = TimeSpan.FromMilliseconds(100);
        var max = TimeSpan.FromSeconds(5);
        
        // Act
        var suspicion = new Suspicion(
            from: "node1",
            k: 0, // No confirmations needed
            min: min,
            max: max,
            timeoutFn: (confirmations) => timeoutCalled = true);
        
        // Wait for min timeout
        await Task.Delay(min + TimeSpan.FromMilliseconds(50));
        
        // Assert
        timeoutCalled.Should().BeTrue("timeout should fire after min duration when k=0");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public async Task NewSuspicion_WithConfirmationsNeeded_ShouldUseMaxTimeout()
    {
        // Arrange
        var timeoutCalled = false;
        var min = TimeSpan.FromMilliseconds(100);
        var max = TimeSpan.FromMilliseconds(500);
        
        // Act
        var suspicion = new Suspicion(
            from: "node1",
            k: 3, // Need 3 confirmations
            min: min,
            max: max,
            timeoutFn: (confirmations) => timeoutCalled = true);
        
        // Wait for less than max timeout
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        
        // Assert
        timeoutCalled.Should().BeFalse("timeout should not fire yet");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public void Confirm_FromNewNode_ShouldReturnTrue()
    {
        // Arrange
        var suspicion = new Suspicion(
            from: "node1",
            k: 3,
            min: TimeSpan.FromSeconds(1),
            max: TimeSpan.FromSeconds(10),
            timeoutFn: (confirmations) => { });
        
        // Act
        var result = suspicion.Confirm("node2");
        
        // Assert
        result.Should().BeTrue("new confirmation should be accepted");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public void Confirm_FromSameNodeTwice_ShouldReturnFalseOnSecond()
    {
        // Arrange
        var suspicion = new Suspicion(
            from: "node1",
            k: 3,
            min: TimeSpan.FromSeconds(1),
            max: TimeSpan.FromSeconds(10),
            timeoutFn: (confirmations) => { });
        
        // Act
        var first = suspicion.Confirm("node2");
        var second = suspicion.Confirm("node2");
        
        // Assert
        first.Should().BeTrue("first confirmation should be accepted");
        second.Should().BeFalse("duplicate confirmation should be rejected");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public void Confirm_FromOriginatingNode_ShouldReturnFalse()
    {
        // Arrange
        var suspicion = new Suspicion(
            from: "node1",
            k: 3,
            min: TimeSpan.FromSeconds(1),
            max: TimeSpan.FromSeconds(10),
            timeoutFn: (confirmations) => { });
        
        // Act
        var result = suspicion.Confirm("node1");
        
        // Assert
        result.Should().BeFalse("originating node's confirmation should be excluded");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public void Confirm_WhenKReached_ShouldReturnFalse()
    {
        // Arrange
        var suspicion = new Suspicion(
            from: "node1",
            k: 2,
            min: TimeSpan.FromSeconds(1),
            max: TimeSpan.FromSeconds(10),
            timeoutFn: (confirmations) => { });
        
        // Act
        suspicion.Confirm("node2");
        suspicion.Confirm("node3");
        var extraConfirmation = suspicion.Confirm("node4");
        
        // Assert
        extraConfirmation.Should().BeFalse("confirmations beyond k should be rejected");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public async Task Confirm_MultipleConfirmations_ShouldAccelerateTimeout()
    {
        // Arrange
        var timeoutCalled = false;
        var confirmationCount = 0;
        var min = TimeSpan.FromMilliseconds(200);
        var max = TimeSpan.FromSeconds(5);
        
        var suspicion = new Suspicion(
            from: "node1",
            k: 3,
            min: min,
            max: max,
            timeoutFn: (confirmations) =>
            {
                timeoutCalled = true;
                confirmationCount = confirmations;
            });
        
        // Act - Add confirmations to accelerate
        suspicion.Confirm("node2");
        suspicion.Confirm("node3");
        suspicion.Confirm("node4");
        
        // Wait for accelerated timeout (should be much less than max)
        await Task.Delay(TimeSpan.FromSeconds(1));
        
        // Assert
        timeoutCalled.Should().BeTrue("timeout should fire faster with confirmations");
        confirmationCount.Should().Be(3, "should track number of confirmations");
        
        suspicion.Dispose();
    }
    
    [Fact]
    public void RemainingSuspicionTime_WithNoConfirmations_ShouldReturnMax()
    {
        // Arrange
        var min = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(10);
        var elapsed = TimeSpan.Zero;
        
        // Act
        var remaining = Suspicion.CalculateRemainingSuspicionTime(
            n: 0, k: 3, elapsed: elapsed, min: min, max: max);
        
        // Assert
        remaining.Should().Be(max);
    }
    
    [Fact]
    public void RemainingSuspicionTime_WithAllConfirmations_ShouldApproachMin()
    {
        // Arrange
        var min = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(10);
        var elapsed = TimeSpan.Zero;
        
        // Act
        var remaining = Suspicion.CalculateRemainingSuspicionTime(
            n: 3, k: 3, elapsed: elapsed, min: min, max: max);
        
        // Assert
        remaining.Should().BeLessOrEqualTo(min + TimeSpan.FromMilliseconds(100), 
            "with all confirmations, should be close to min");
    }
    
    [Fact]
    public void RemainingSuspicionTime_WithElapsedTime_ShouldSubtractElapsed()
    {
        // Arrange
        var min = TimeSpan.FromSeconds(1);
        var max = TimeSpan.FromSeconds(10);
        var elapsed = TimeSpan.FromSeconds(5);
        
        // Act
        var remaining = Suspicion.CalculateRemainingSuspicionTime(
            n: 0, k: 3, elapsed: elapsed, min: min, max: max);
        
        // Assert
        remaining.Should().Be(TimeSpan.FromSeconds(5), "elapsed time should be subtracted");
    }
}
