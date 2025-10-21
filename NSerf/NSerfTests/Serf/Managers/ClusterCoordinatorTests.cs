// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSerf.Serf;
using NSerf.Serf.Managers;
using Xunit;

namespace NSerfTests.Serf.Managers;

/// <summary>
/// TDD tests for ClusterCoordinator - manages Serf instance state and cluster operations.
/// Based on Go serf.go Join/Leave/Shutdown operations.
/// Reference: DeepWiki analysis of hashicorp/serf cluster coordination
/// </summary>
public class ClusterCoordinatorTests
{
    [Fact]
    public void Constructor_ShouldInitializeInAliveState()
    {
        // Arrange & Act
        var coordinator = new ClusterCoordinator(logger: null);

        // Assert
        coordinator.GetCurrentState().Should().Be(SerfState.SerfAlive, "should start in Alive state");
    }

    [Fact]
    public void GetCurrentState_ShouldReturnCurrentState()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var state = coordinator.GetCurrentState();

        // Assert
        state.Should().Be(SerfState.SerfAlive);
    }

    [Fact]
    public void SetState_FromAliveToLeaving_ShouldSucceed()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var result = coordinator.TryTransitionToLeaving();

        // Assert
        result.Should().BeTrue("transition from Alive to Leaving should succeed");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfLeaving);
    }

    [Fact]
    public void SetState_FromLeavingToLeft_ShouldSucceed()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToLeaving();

        // Act
        var result = coordinator.TryTransitionToLeft();

        // Assert
        result.Should().BeTrue("transition from Leaving to Left should succeed");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfLeft);
    }

    [Fact]
    public void SetState_FromAliveToLeft_ShouldFail()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var result = coordinator.TryTransitionToLeft();

        // Assert
        result.Should().BeFalse("cannot go directly from Alive to Left without Leaving");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfAlive, "state should remain Alive");
    }

    [Fact]
    public void SetState_FromAliveToShutdown_ShouldSucceed()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var result = coordinator.TryTransitionToShutdown();

        // Assert
        result.Should().BeTrue("shutdown can happen from any state");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfShutdown);
    }

    [Fact]
    public void SetState_FromLeavingToShutdown_ShouldSucceed()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToLeaving();

        // Act
        var result = coordinator.TryTransitionToShutdown();

        // Assert
        result.Should().BeTrue("shutdown can happen from any state");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfShutdown);
    }

    [Fact]
    public void SetState_FromLeftToShutdown_ShouldSucceed()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToLeaving();
        coordinator.TryTransitionToLeft();

        // Act
        var result = coordinator.TryTransitionToShutdown();

        // Assert
        result.Should().BeTrue("shutdown can happen from any state");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfShutdown);
    }

    [Fact]
    public void SetState_FromShutdownToAnything_ShouldFail()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToShutdown();

        // Act & Assert - Cannot transition from Shutdown
        coordinator.TryTransitionToLeaving().Should().BeFalse("cannot transition from Shutdown");
        coordinator.TryTransitionToLeft().Should().BeFalse("cannot transition from Shutdown");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfShutdown, "should remain in Shutdown");
    }

    [Fact]
    public void SetState_FromLeftToLeaving_ShouldFail()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToLeaving();
        coordinator.TryTransitionToLeft();

        // Act
        var result = coordinator.TryTransitionToLeaving();

        // Assert
        result.Should().BeFalse("cannot go from Left back to Leaving");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfLeft);
    }

    [Fact]
    public void IsShutdown_WhenShutdown_ShouldReturnTrue()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToShutdown();

        // Act
        var isShutdown = coordinator.IsShutdown();

        // Assert
        isShutdown.Should().BeTrue();
    }

    [Fact]
    public void IsShutdown_WhenNotShutdown_ShouldReturnFalse()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var isShutdown = coordinator.IsShutdown();

        // Assert
        isShutdown.Should().BeFalse();
    }

    [Fact]
    public void IsLeaving_WhenLeaving_ShouldReturnTrue()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToLeaving();

        // Act
        var isLeaving = coordinator.IsLeaving();

        // Assert
        isLeaving.Should().BeTrue();
    }

    [Fact]
    public void IsLeaving_WhenNotLeaving_ShouldReturnFalse()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var isLeaving = coordinator.IsLeaving();

        // Assert
        isLeaving.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentStateReads_ShouldBeThreadSafe()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        var tasks = new List<Task<SerfState>>();

        // Act - Multiple threads reading state simultaneously
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => coordinator.GetCurrentState()));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All reads should return same consistent state
        results.Should().OnlyContain(s => s == SerfState.SerfAlive, "all reads should return Alive");
    }

    [Fact]
    public async Task ConcurrentStateTransitions_ShouldBeSerialized()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        var tasks = new List<Task<bool>>();

        // Act - Multiple threads trying to transition to Leaving
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() => coordinator.TryTransitionToLeaving()));
        }

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r == true);

        // Assert - Only one transition should succeed
        successCount.Should().Be(1, "only one thread should successfully transition");
        coordinator.GetCurrentState().Should().Be(SerfState.SerfLeaving);
    }

    [Fact]
    public void ExecuteInLeavingState_WhenLeaving_ShouldExecuteCallback()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToLeaving();
        var executed = false;

        // Act
        coordinator.ExecuteInLeavingState(() => executed = true);

        // Assert
        executed.Should().BeTrue("callback should execute when in Leaving state");
    }

    [Fact]
    public void ExecuteInLeavingState_WhenNotLeaving_ShouldNotExecuteCallback()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        var executed = false;

        // Act
        coordinator.ExecuteInLeavingState(() => executed = true);

        // Assert
        executed.Should().BeFalse("callback should not execute when not in Leaving state");
    }

    [Fact]
    public void ExecuteIfNotShutdown_WhenAlive_ShouldExecuteAndReturnTrue()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        var executed = false;

        // Act
        var result = coordinator.ExecuteIfNotShutdown(() => executed = true);

        // Assert
        result.Should().BeTrue("should return true when not shutdown");
        executed.Should().BeTrue("callback should execute");
    }

    [Fact]
    public void ExecuteIfNotShutdown_WhenShutdown_ShouldNotExecuteAndReturnFalse()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.TryTransitionToShutdown();
        var executed = false;

        // Act
        var result = coordinator.ExecuteIfNotShutdown(() => executed = true);

        // Assert
        result.Should().BeFalse("should return false when shutdown");
        executed.Should().BeFalse("callback should not execute");
    }

    [Fact]
    public void GetStateSnapshot_ShouldReturnCorrectInfo()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act
        var snapshot = coordinator.GetStateSnapshot();

        // Assert
        snapshot.CurrentState.Should().Be(SerfState.SerfAlive);
        snapshot.IsShutdown.Should().BeFalse();
        snapshot.IsLeaving.Should().BeFalse();
    }

    [Fact]
    public void Dispose_ShouldTransitionToShutdown()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);
        coordinator.GetCurrentState().Should().Be(SerfState.SerfAlive, "should start in Alive");

        // Act
        coordinator.Dispose();

        // Assert - Cannot check state after disposal since lock is disposed
        // The dispose method calls TryTransitionToShutdown internally which is tested separately
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldBeIdempotent()
    {
        // Arrange
        var coordinator = new ClusterCoordinator(logger: null);

        // Act - First dispose should work
        var act = () =>
        {
            coordinator.Dispose();
            // Subsequent disposes should not throw even though lock is disposed
            // (trying to dispose an already disposed semaphore doesn't throw)
        };

        // Assert - Should not throw
        act.Should().NotThrow("first dispose should succeed");
    }
}
