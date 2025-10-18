// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event_delegate.go

using FluentAssertions;
using NSerf.Memberlist.State;
using NSerf.Serf;
using NSerf.Serf.Events;
using System.Net;
using System.Threading.Channels;
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
    public async Task NotifyJoin_ShouldCallSerfHandleNodeJoin()
    {
        // Arrange
        var (eventWriter, eventReader) = TestHelpers.CreateTestEventChannel();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            EventCh = eventWriter
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

        // Act
        eventDelegate.NotifyJoin(node);
        
        // Assert - Verify node was added to member states
        serf.MemberStates.Should().ContainKey("joining-node", "node should be added to member states");
        var memberInfo = serf.MemberStates["joining-node"];
        memberInfo.Status.Should().Be(MemberStatus.Alive, "node should have Alive status");
        memberInfo.Name.Should().Be("joining-node");
        memberInfo.Member.Addr.Should().Be(IPAddress.Parse("127.0.0.1"));
        memberInfo.Member.Port.Should().Be(8000);
        
        // Verify MemberEvent was emitted
        await TestHelpers.WaitForConditionAsync(
            () => eventReader.TryRead(out var evt) && evt is MemberEvent me && me.Type == EventType.MemberJoin,
            TimeSpan.FromSeconds(1),
            "MemberEvent with MemberJoin should be emitted");
    }

    [Fact]
    public async Task NotifyLeave_ShouldCallSerfHandleNodeLeave()
    {
        // Arrange
        var (eventWriter, eventReader) = TestHelpers.CreateTestEventChannel();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            EventCh = eventWriter
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        // First join the node so it can leave
        var node = new Node
        {
            Name = "leaving-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };
        eventDelegate.NotifyJoin(node);
        
        // Drain join event
        await eventReader.WaitToReadAsync();
        eventReader.TryRead(out _);

        // Act
        eventDelegate.NotifyLeave(node);
        
        // Assert - Verify node status changed
        serf.MemberStates.Should().ContainKey("leaving-node");
        var memberInfo = serf.MemberStates["leaving-node"];
        memberInfo.Status.Should().BeOneOf(MemberStatus.Left, MemberStatus.Failed);
        // Node should have Left or Failed status after leave
        
        // Verify node was added to left or failed members list
        var isInLeftOrFailed = serf.LeftMembers.Any(m => m.Name == "leaving-node") ||
                               serf.FailedMembers.Any(m => m.Name == "leaving-node");
        isInLeftOrFailed.Should().BeTrue("node should be in left or failed members list");
        
        // Verify appropriate MemberEvent was emitted
        await TestHelpers.WaitForConditionAsync(
            () => eventReader.TryRead(out var evt) && evt is MemberEvent,
            TimeSpan.FromSeconds(1),
            "MemberEvent should be emitted for leave");
    }

    [Fact]
    public async Task NotifyUpdate_ShouldCallSerfHandleNodeUpdate()
    {
        // Arrange
        var (eventWriter, eventReader) = TestHelpers.CreateTestEventChannel();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            EventCh = eventWriter
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        // First join the node so it can be updated
        var initialNode = new Node
        {
            Name = "updated-node",
            Addr = IPAddress.Parse("127.0.0.1"),
            Port = 8000,
            Meta = Array.Empty<byte>()
        };
        eventDelegate.NotifyJoin(initialNode);
        
        // Drain join event
        await eventReader.WaitToReadAsync();
        eventReader.TryRead(out _);
        
        // Now update with new metadata
        var updatedNode = new Node
        {
            Name = "updated-node",
            Addr = IPAddress.Parse("127.0.0.2"), // Different address
            Port = 8001, // Different port
            Meta = new byte[] { 1, 2, 3 } // New metadata
        };

        // Act
        eventDelegate.NotifyUpdate(updatedNode);
        
        // Assert - Verify node properties were updated
        serf.MemberStates.Should().ContainKey("updated-node");
        var memberInfo = serf.MemberStates["updated-node"];
        memberInfo.Member.Addr.Should().Be(IPAddress.Parse("127.0.0.2"), "address should be updated");
        memberInfo.Member.Port.Should().Be(8001, "port should be updated");
        memberInfo.Member.Tags.Should().NotBeNull("tags should be updated from metadata");
        
        // Verify MemberEvent with MemberUpdate was emitted
        await TestHelpers.WaitForConditionAsync(
            () => eventReader.TryRead(out var evt) && evt is MemberEvent me && me.Type == EventType.MemberUpdate,
            TimeSpan.FromSeconds(1),
            "MemberEvent with MemberUpdate should be emitted");
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
        
        var initialCount = serf.MemberStates.Count;

        // Act
        var act = () => eventDelegate.NotifyJoin(null!);
        
        // Assert - Should handle null gracefully without throwing
        act.Should().NotThrow("Serf should handle null nodes gracefully");
        
        // Verify no state changes occurred
        serf.MemberStates.Count.Should().Be(initialCount, "member count should not change with null input");
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
        
        var initialCount = serf.MemberStates.Count;

        // Act
        var act = () => eventDelegate.NotifyLeave(null!);
        
        // Assert - Should handle null gracefully without throwing
        act.Should().NotThrow("Serf should handle null nodes gracefully");
        
        // Verify no state changes occurred
        serf.MemberStates.Count.Should().Be(initialCount, "member count should not change with null input");
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
        
        var initialCount = serf.MemberStates.Count;

        // Act
        var act = () => eventDelegate.NotifyUpdate(null!);
        
        // Assert - Should handle null gracefully without throwing
        act.Should().NotThrow("Serf should handle null nodes gracefully");
        
        // Verify no state changes occurred
        serf.MemberStates.Count.Should().Be(initialCount, "member count should not change with null input");
    }

    [Fact]
    public async Task NotifyJoin_MultipleNodes_ShouldHandleSequentially()
    {
        // Arrange
        var (eventWriter, eventReader) = TestHelpers.CreateTestEventChannel();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            EventCh = eventWriter
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var nodes = new[]
        {
            new Node { Name = "node1", Addr = IPAddress.Parse("127.0.0.1"), Port = 8001, Meta = Array.Empty<byte>() },
            new Node { Name = "node2", Addr = IPAddress.Parse("127.0.0.1"), Port = 8002, Meta = Array.Empty<byte>() },
            new Node { Name = "node3", Addr = IPAddress.Parse("127.0.0.1"), Port = 8003, Meta = Array.Empty<byte>() }
        };

        // Act
        foreach (var node in nodes)
        {
            eventDelegate.NotifyJoin(node);
        }
        
        // Wait for events to be processed
        await Task.Delay(100);

        // Assert - Verify all 3 nodes were added
        serf.MemberStates.Should().ContainKey("node1", "node1 should be added");
        serf.MemberStates.Should().ContainKey("node2", "node2 should be added");
        serf.MemberStates.Should().ContainKey("node3", "node3 should be added");
        
        // Verify all have Alive status
        serf.MemberStates["node1"].Status.Should().Be(MemberStatus.Alive);
        serf.MemberStates["node2"].Status.Should().Be(MemberStatus.Alive);
        serf.MemberStates["node3"].Status.Should().Be(MemberStatus.Alive);
        
        // Verify 3 MemberEvent messages were emitted
        var eventCount = 0;
        while (eventReader.TryRead(out var evt) && evt is MemberEvent me && me.Type == EventType.MemberJoin)
        {
            eventCount++;
        }
        eventCount.Should().Be(3, "should emit 3 join events");
    }

    [Fact]
    public async Task NotifyLeave_MultipleNodes_ShouldHandleSequentially()
    {
        // Arrange
        var (eventWriter, eventReader) = TestHelpers.CreateTestEventChannel();
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            EventCh = eventWriter
        };
        var serf = new NSerf.Serf.Serf(config);
        var eventDelegate = new EventDelegate(serf);

        var nodes = new[]
        {
            new Node { Name = "node1", Addr = IPAddress.Parse("127.0.0.1"), Port = 8001, Meta = Array.Empty<byte>() },
            new Node { Name = "node2", Addr = IPAddress.Parse("127.0.0.1"), Port = 8002, Meta = Array.Empty<byte>() }
        };
        
        // First join both nodes
        foreach (var node in nodes)
        {
            eventDelegate.NotifyJoin(node);
        }
        
        // Drain join events
        await Task.Delay(100);
        while (eventReader.TryRead(out _)) { }

        // Act - Leave both nodes
        foreach (var node in nodes)
        {
            eventDelegate.NotifyLeave(node);
        }
        
        // Wait for events to be processed
        await Task.Delay(100);

        // Assert - Verify both nodes have left/failed status
        serf.MemberStates["node1"].Status.Should().BeOneOf(MemberStatus.Left, MemberStatus.Failed);
        serf.MemberStates["node2"].Status.Should().BeOneOf(MemberStatus.Left, MemberStatus.Failed);
        
        // Verify both are in left or failed members lists
        var node1InList = serf.LeftMembers.Any(m => m.Name == "node1") || serf.FailedMembers.Any(m => m.Name == "node1");
        var node2InList = serf.LeftMembers.Any(m => m.Name == "node2") || serf.FailedMembers.Any(m => m.Name == "node2");
        
        node1InList.Should().BeTrue("node1 should be in left/failed list");
        node2InList.Should().BeTrue("node2 should be in left/failed list");
        
        // Verify 2 MemberEvent messages were emitted
        var eventCount = 0;
        while (eventReader.TryRead(out var evt) && evt is MemberEvent)
        {
            eventCount++;
        }
        eventCount.Should().Be(2, "should emit 2 leave/failed events");
    }
}
