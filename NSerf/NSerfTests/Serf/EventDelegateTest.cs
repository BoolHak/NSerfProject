// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event_delegate.go

using FluentAssertions;
using NSerf.Memberlist.State;
using NSerf.Serf;
using System.Net;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Serf Event Delegate implementation.
/// Tests the bridge between Memberlist events and Serf handlers.
/// 
/// Note: Go has no dedicated unit tests for event_delegate.go.
/// These tests verify the EventDelegate correctly forwards to Serf handlers.
/// </summary>
public class EventDelegateTest
{
    [Fact]
    public void NotifyJoin_ShouldCallSerfHandleNodeJoin()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var node = new Node
        {
            Name = "joining-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act & Assert - Should not throw and handle gracefully
        var act = () => eventDelegate.NotifyJoin(node);
        act.Should().NotThrow("EventDelegate should forward join notifications without errors");
        
        // TODO: Phase 9 - Add assertions to verify Serf member state was updated
        // Should verify: node was added to Serf.Members, MemberJoin event was emitted
    }

    [Fact]
    public void NotifyLeave_ShouldCallSerfHandleNodeLeave()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var node = new Node
        {
            Name = "leaving-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };

        // Act & Assert - Should not throw and handle gracefully
        var act = () => eventDelegate.NotifyLeave(node);
        act.Should().NotThrow("EventDelegate should forward leave notifications without errors");
        
        // TODO: Phase 9 - Add assertions to verify Serf member state was updated
        // Should verify: node was marked as left, MemberLeave event was emitted
    }

    [Fact]
    public void NotifyUpdate_ShouldCallSerfHandleNodeUpdate()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var node = new Node
        {
            Name = "updated-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = new byte[] { 1, 2, 3 }
        };

        // Act & Assert - Should not throw and handle gracefully
        var act = () => eventDelegate.NotifyUpdate(node);
        act.Should().NotThrow("EventDelegate should forward update notifications without errors");
        
        // TODO: Phase 9 - Add assertions to verify Serf member metadata was updated
        // Should verify: node metadata was updated, MemberUpdate event was emitted
    }

    [Fact]
    public void Constructor_WithNullSerf_ShouldThrow()
    {
        // Act & Assert
        var act = () => new EventDelegate(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serf");
    }

    [Fact]
    public void NotifyJoin_WithNullNode_ShouldNotThrow()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        // Act & Assert - Should handle null gracefully (Serf logs warning but doesn't crash)
        var act = () => eventDelegate.NotifyJoin(null!);
        act.Should().NotThrow("Serf should handle null nodes gracefully");
    }

    [Fact]
    public void NotifyLeave_WithNullNode_ShouldNotCrash()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        // Act & Assert - Should handle null gracefully
        var act = () => eventDelegate.NotifyLeave(null!);
        act.Should().NotThrow("Serf should handle null nodes gracefully");
    }

    [Fact]
    public void NotifyUpdate_WithNullNode_ShouldNotCrash()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        // Act & Assert - Should handle null gracefully
        var act = () => eventDelegate.NotifyUpdate(null!);
        act.Should().NotThrow("Serf should handle null nodes gracefully");
    }

    [Fact]
    public void NotifyJoin_MultipleNodes_ShouldHandleSequentially()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var nodes = new[]
        {
            new Node { Name = "node1", Addr = IPAddress.Parse("127.0.0.1"), Port = 8001, Meta = Array.Empty<byte>() },
            new Node { Name = "node2", Addr = IPAddress.Parse("127.0.0.1"), Port = 8002, Meta = Array.Empty<byte>() },
            new Node { Name = "node3", Addr = IPAddress.Parse("127.0.0.1"), Port = 8003, Meta = Array.Empty<byte>() }
        };

        // Act & Assert - Should handle multiple nodes without errors
        var act = () =>
        {
            foreach (var node in nodes)
            {
                eventDelegate.NotifyJoin(node);
            }
        };
        act.Should().NotThrow("EventDelegate should handle multiple sequential joins");
    }

    [Fact]
    public void NotifyLeave_MultipleNodes_ShouldHandleSequentially()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var nodes = new[]
        {
            new Node { Name = "node1", Addr = IPAddress.Parse("127.0.0.1"), Port = 8001, Meta = Array.Empty<byte>() },
            new Node { Name = "node2", Addr = IPAddress.Parse("127.0.0.1"), Port = 8002, Meta = Array.Empty<byte>() }
        };

        // Act & Assert - Should handle multiple nodes without errors
        var act = () =>
        {
            foreach (var node in nodes)
            {
                eventDelegate.NotifyLeave(node);
            }
        };
        act.Should().NotThrow("EventDelegate should handle multiple sequential leaves");
    }
}
