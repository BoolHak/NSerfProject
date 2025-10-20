// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/delegate_test.go

using FluentAssertions;
using MessagePack;
using NSerf.Serf;
using Xunit;
using SerfDelegate = NSerf.Serf.Delegate;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Serf Delegate implementation.
/// Validates gossip message routing, tag encoding, and state synchronization.
/// </summary>
public class DelegateTest
{
    [Fact]
    public void NodeMeta_ProtocolV2_ShouldEncodeRoleAsRawString()
    {
        // Arrange - Protocol v2 uses raw "role" string encoding
        var config = new Config
        {
            NodeName = "test-node",
            ProtocolVersion = 2,
            Tags = new Dictionary<string, string> { ["role"] = "test" }
        };

        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Act
        var meta = delegateObj.NodeMeta(32);

        // Assert - Should be raw bytes "test"
        var expected = System.Text.Encoding.UTF8.GetBytes("test");
        meta.Should().BeEquivalentTo(expected, "protocol v2 encodes role as raw string");

        // Decode should recover the role
        var decoded = serf.DecodeTags(meta);
        decoded.Should().ContainKey("role");
        decoded["role"].Should().Be("test");
    }

    [Fact]
    public void NodeMeta_ProtocolV2_ShouldPanicOnExceedingLimit()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            ProtocolVersion = 2,
            Tags = new Dictionary<string, string> { ["role"] = "test" }
        };

        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Act & Assert - Should throw when limit is too small
        var act = () => delegateObj.NodeMeta(1);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exceeds length limit*");
    }

    [Fact]
    public void NodeMeta_ProtocolV3_ShouldEncodeWithMagicByte()
    {
        // Arrange - Protocol v3 uses magic byte + MessagePack
        var config = new Config
        {
            NodeName = "test-node",
            ProtocolVersion = 3,
            Tags = new Dictionary<string, string> { ["role"] = "test" }
        };

        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Act
        var meta = delegateObj.NodeMeta(32);

        // Assert - Should have magic byte prefix
        meta[0].Should().Be(255, "protocol v3 uses magic byte 255");

        // Decode should recover the tags
        var decoded = serf.DecodeTags(meta);
        decoded.Should().ContainKey("role");
        decoded["role"].Should().Be("test");
    }

    [Fact]
    public void NodeMeta_ProtocolV3_ShouldPanicOnExceedingLimit()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            ProtocolVersion = 3,
            Tags = new Dictionary<string, string> { ["role"] = "test" }
        };

        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Act & Assert - Should throw when limit is too small
        var act = () => delegateObj.NodeMeta(1);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exceeds length limit*");
    }

    [Fact]
    public void LocalState_ShouldSerializeCurrentState()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Manually set some state for testing
        serf.Clock.Increment();
        serf.Clock.Increment();
        var currentLTime = serf.Clock.Time();

        serf.EventClock.Increment();
        var currentEventLTime = serf.EventClock.Time();

        serf.QueryClock.Increment();
        var currentQueryLTime = serf.QueryClock.Time();

        // Add a test member
        var testMember = new MemberInfo
        {
            Name = "test-node",
            StateMachine = new NSerf.Serf.StateMachine.MemberStateMachine(
                "test-node",
                MemberStatus.Alive,
                5,
                null)
        };
        serf.AddMember(testMember);

        // Act
        var stateBytes = delegateObj.LocalState(join: false);

        // Assert
        stateBytes.Should().NotBeEmpty();

        // First byte should be message type
        var messageType = (MessageType)stateBytes[0];
        messageType.Should().Be(MessageType.PushPull);

        // Decode the message
        var pushPull = MessagePackSerializer.Deserialize<MessagePushPull>(stateBytes.AsMemory(1));

        // Verify clocks were captured
        pushPull.LTime.Should().Be(currentLTime);
        pushPull.EventLTime.Should().Be(currentEventLTime);
        pushPull.QueryLTime.Should().Be(currentQueryLTime);

        // Verify member status
        pushPull.StatusLTimes.Should().ContainKey("test-node");
        pushPull.StatusLTimes["test-node"].Should().Be(5);

        // Verify left members list exists
        pushPull.LeftMembers.Should().NotBeNull();
        
        // Verify events are included in serialization
        // Add a test event via HandleUserEvent (which adds to EventManager's buffer)
        var userEventMsg = new MessageUserEvent
        {
            LTime = 100,
            Name = "test-event",
            Payload = new byte[] { 1, 2, 3 },
            CC = false
        };
        serf.HandleUserEvent(userEventMsg);
        
        // Serialize again with event
        var stateWithEvent = delegateObj.LocalState(join: false);
        var pushPullWithEvent = MessagePackSerializer.Deserialize<MessagePushPull>(stateWithEvent.AsMemory(1));
        
        // Verify EventManager events are included in push/pull state
        pushPullWithEvent.Events.Should().NotBeNull("Events should be serialized");
        pushPullWithEvent.Events.Should().Contain(e => e.LTime == 100, 
            "EventManager events should be included in serialized state");
        
        var serializedEvent = pushPullWithEvent.Events.First(e => e.LTime == 100);
        serializedEvent.Events.Should().ContainSingle("should have one event");
        serializedEvent.Events[0].Name.Should().Be("test-event");
        serializedEvent.Events[0].Payload.Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void MergeRemoteState_ShouldIntegrateRemoteState()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Create a fake push/pull message
        var pushPull = new MessagePushPull
        {
            LTime = 42,
            StatusLTimes = new Dictionary<string, LamportTime>
            {
                ["test"] = 20,
                ["foo"] = 15
            },
            LeftMembers = new List<string> { "foo" },
            EventLTime = 50,
            Events = new List<UserEventCollection>
            {
                new UserEventCollection
                {
                    LTime = 45,
                    Events = new List<UserEventData>
                    {
                        new UserEventData
                        {
                            Name = "test-event",
                            Payload = Array.Empty<byte>()
                        }
                    }
                }
            },
            QueryLTime = 100
        };

        // Encode the message
        var payload = MessagePackSerializer.Serialize(pushPull);
        var buffer = new byte[payload.Length + 1];
        buffer[0] = (byte)MessageType.PushPull;
        Array.Copy(payload, 0, buffer, 1, payload.Length);

        // Act
        delegateObj.MergeRemoteState(buffer, join: false);

        // Assert - Clocks should be witnessed (minus 1 as per protocol)
        serf.Clock.Time().Should().BeGreaterOrEqualTo(41, "should witness LTime - 1");
        serf.EventClock.Time().Should().BeGreaterOrEqualTo(49, "should witness EventLTime - 1");
        serf.QueryClock.Time().Should().BeGreaterOrEqualTo(99, "should witness QueryLTime - 1");
        
        // Verify StatusLTimes were applied to member states
        // "test" should be processed as join intent
        serf.HasMember("test").Should().BeTrue("StatusLTime for 'test' should create/update member");
        serf.GetMember("test")!.StatusLTime.Should().Be(20, "StatusLTime should be applied");
        
        // Verify LeftMembers were processed
        // "foo" is in LeftMembers, so it should be marked as leaving/left
        serf.HasMember("foo").Should().BeTrue("LeftMember 'foo' should be processed");
        serf.GetMember("foo")!.Status.Should().BeOneOf(MemberStatus.Leaving, MemberStatus.Left);
        // foo should be marked as Leaving or Left
        
        // Verify Events were queued to EventManager
        // Event with LTime=45 should be added to EventManager's buffer
        var events = serf._eventManager?.GetEventCollectionsForPushPull() ?? new List<UserEventCollection>();
        events.Should().Contain(e => e.LTime == 45, "Event should be queued to EventManager");
        var queuedEvent = events.First(e => e.LTime == 45);
        queuedEvent.Events.Should().ContainSingle("should have one event");
        queuedEvent.Events[0].Name.Should().Be("test-event", "event name should match");
        queuedEvent.Events[0].Payload.Should().Equal(Array.Empty<byte>(), "event payload should match");
    }

    [Fact]
    public void MergeRemoteState_WithBadType_ShouldLogErrorAndReturn()
    {
        // Arrange
        var config = new Config { NodeName = "test-node", Tags = new Dictionary<string, string>() };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Create buffer with wrong message type
        var buffer = new byte[] { (byte)MessageType.Join, 1, 2, 3 };

        // Act - Should not throw, just log error
        delegateObj.MergeRemoteState(buffer, join: false);

        // Assert - State should be unchanged
        serf.Clock.Time().Should().Be(0, "clock should not change with bad message type");
    }

    [Fact]
    public void MergeRemoteState_WithEmptyBuffer_ShouldLogErrorAndReturn()
    {
        // Arrange
        var config = new Config { NodeName = "test-node", Tags = new Dictionary<string, string>() };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Act - Should not throw, just log error
        delegateObj.MergeRemoteState(Array.Empty<byte>(), join: false);

        // Assert - State should be unchanged
        serf.Clock.Time().Should().Be(0, "clock should not change with empty buffer");
    }

    [Fact]
    public void GetBroadcasts_ShouldCollectFromAllQueues()
    {
        // Arrange
        var config = new Config { NodeName = "test-node", Tags = new Dictionary<string, string>() };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);

        // Queue some broadcasts
        serf.Broadcasts.QueueBytes(new byte[] { 1, 2, 3 });
        serf.EventBroadcasts.QueueBytes(new byte[] { 4, 5, 6 });
        serf.QueryBroadcasts.QueueBytes(new byte[] { 7, 8, 9 });

        // Act
        var broadcasts = delegateObj.GetBroadcasts(overhead: 10, limit: 1000);

        // Assert - Should collect from all three queues
        broadcasts.Should().NotBeEmpty();
        broadcasts.Should().HaveCountGreaterOrEqualTo(1, "should have at least one broadcast");
    }

    [Fact]
    public void NotifyMsg_WithLeaveMessage_ShouldRouteToHandler()
    {
        // Arrange
        var config = new Config { NodeName = "test-node", Tags = new Dictionary<string, string>() };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);
        
        // Add the node first so leave intent can be processed
        serf.AddMember(new MemberInfo
        {
            Name = "leaving-node",
            StateMachine = new NSerf.Serf.StateMachine.MemberStateMachine(
                "leaving-node",
                MemberStatus.Alive,
                5,
                null)
        });

        var leaveMsg = new MessageLeave
        {
            LTime = 10,
            Node = "leaving-node"
        };

        var payload = MessagePackSerializer.Serialize(leaveMsg);
        var buffer = new byte[payload.Length + 1];
        buffer[0] = (byte)MessageType.Leave;
        Array.Copy(payload, 0, buffer, 1, payload.Length);
        
        var initialClockTime = serf.Clock.Time();

        // Act
        delegateObj.NotifyMsg(buffer);

        // Assert - Verify HandleNodeLeaveIntent was called
        // 1. Clock should have witnessed the LTime
        serf.Clock.Time().Should().BeGreaterOrEqualTo(10, "clock should witness LTime from leave message");
        serf.Clock.Time().Should().BeGreaterThan(initialClockTime, "clock should have advanced");
        
        // 2. Member status should be updated to Leaving (Alive -> Leaving transition)
        serf.HasMember("leaving-node").Should().BeTrue();
        var memberInfo = serf.GetMember("leaving-node");
        memberInfo.Should().NotBeNull();
        memberInfo!.Status.Should().Be(MemberStatus.Leaving, 
            "member should transition from Alive to Leaving after leave intent");
        memberInfo.StatusLTime.Should().Be(10, 
            "StatusLTime should be updated to leave message LTime");
    }

    [Fact]
    public void NotifyMsg_WithEmptyBuffer_ShouldReturnEarly()
    {
        // Arrange
        var config = new Config { NodeName = "test-node", Tags = new Dictionary<string, string>() };
        var serf = new NSerf.Serf.Serf(config);
        var delegateObj = new SerfDelegate(serf);
        
        var initialClockTime = serf.Clock.Time();
        var initialMemberCount = serf.NumMembers();

        // Act
        delegateObj.NotifyMsg(Array.Empty<byte>());

        // Assert - Verify no handlers were called, no state changes
        // 1. Clock should not change
        serf.Clock.Time().Should().Be(initialClockTime, "clock should not change with empty buffer");
        
        // 2. No members added or changed
        serf.NumMembers().Should().Be(initialMemberCount, "member count should not change");
        
        // Empty buffer should be handled gracefully without throwing
    }
}
