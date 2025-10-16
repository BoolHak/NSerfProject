// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/conflict_delegate.go

using FluentAssertions;
using NSerf.Memberlist.State;
using NSerf.Serf;
using System.Net;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Serf Conflict Delegate implementation.
/// Tests the bridge between Memberlist conflict detection and Serf handlers.
/// 
/// Note: Go has no dedicated unit tests for conflict_delegate.go.
/// These tests verify the ConflictDelegate correctly forwards to Serf conflict handler.
/// </summary>
public class ConflictDelegateTest
{
    [Fact]
    public void NotifyConflict_ShouldCallSerfHandleNodeConflict()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var existingNode = new Node
        {
            Name = "conflicted-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        var otherNode = new Node
        {
            Name = "conflicted-node", // Same name!
            Addr = IPAddress.Parse("127.0.0.2"),
            Port = 8001,
            Meta = Array.Empty<byte>()
        };

        // Act & Assert - Should not throw and handle conflict gracefully
        var act = () => conflictDelegate.NotifyConflict(existingNode, otherNode);
        act.Should().NotThrow("ConflictDelegate should forward conflict notifications without errors");
        
        // TODO: Phase 9 - Add assertions to verify conflict resolution behavior
        // Should verify: conflict was logged, appropriate action was taken (e.g., node rejected)
    }

    [Fact]
    public void Constructor_WithNullSerf_ShouldThrow()
    {
        // Act & Assert
        var act = () => new ConflictDelegate(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serf");
    }

    [Fact]
    public void NotifyConflict_WithSameNameDifferentAddress_ShouldHandle()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var existingNode = new Node
        {
            Name = "duplicate",
            Addr = IPAddress.Parse("10.0.0.1"),
            Port = 7946,
            Meta = new byte[] { 1, 2, 3 }
        };

        var otherNode = new Node
        {
            Name = "duplicate",
            Addr = IPAddress.Parse("10.0.0.2"),
            Port = 7946,
            Meta = new byte[] { 4, 5, 6 }
        };

        // Act - Should handle name conflict
        var act = () => conflictDelegate.NotifyConflict(existingNode, otherNode);

        // Assert - Should not throw
        act.Should().NotThrow();
        
        // TODO: Phase 9 - Add assertions to verify which node was kept/rejected
        // Should verify: conflict resolution policy was applied correctly
    }

    [Fact]
    public void NotifyConflict_WithNullExisting_ShouldNotCrash()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var otherNode = new Node
        {
            Name = "node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act & Assert - Should handle null gracefully (Serf logs warning but doesn't crash)
        var act = () => conflictDelegate.NotifyConflict(null!, otherNode);
        act.Should().NotThrow("Serf should handle null nodes in conflict gracefully");
    }

    [Fact]
    public void NotifyConflict_WithNullOther_ShouldNotCrash()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var existingNode = new Node
        {
            Name = "node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act & Assert - Should handle null gracefully
        var act = () => conflictDelegate.NotifyConflict(existingNode, null!);
        act.Should().NotThrow("Serf should handle null nodes in conflict gracefully");
    }

    [Fact]
    public void NotifyConflict_MultipleConflicts_ShouldHandleSequentially()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var conflictDelegate = new ConflictDelegate(serf);

        var conflicts = new[]
        {
            (new Node { Name = "node1", Addr = IPAddress.Parse("10.0.0.1"), Port = 8000, Meta = Array.Empty<byte>() },
             new Node { Name = "node1", Addr = IPAddress.Parse("10.0.0.2"), Port = 8000, Meta = Array.Empty<byte>() }),
            
            (new Node { Name = "node2", Addr = IPAddress.Parse("10.0.0.3"), Port = 8000, Meta = Array.Empty<byte>() },
             new Node { Name = "node2", Addr = IPAddress.Parse("10.0.0.4"), Port = 8000, Meta = Array.Empty<byte>() })
        };

        // Act & Assert - Should handle multiple conflicts without errors
        var act = () =>
        {
            foreach (var (existing, other) in conflicts)
            {
                conflictDelegate.NotifyConflict(existing, other);
            }
        };
        act.Should().NotThrow("ConflictDelegate should handle multiple sequential conflicts");
    }
}
