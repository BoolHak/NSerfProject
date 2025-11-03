// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event_test.go

using NSerf.Serf;
using NSerf.Serf.Events;

namespace NSerfTests.Serf.Events;

/// <summary>
/// Tests for Event infrastructure.
/// Accurately ported from Go's event_test.go
/// </summary>
public class EventTest
{
    [Fact]
    public void MemberEvent_AllEventTypes_ShouldHaveCorrectStringRepresentation()
    {
        // Test MemberJoin
        var me = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member>()
        };

        me.EventType().Should().Be(EventType.MemberJoin, "EventType() should return MemberJoin");
        me.ToString().Should().Be("member-join", "ToString() should return 'member-join'");

        // Test MemberLeave
        me.Type = EventType.MemberLeave;
        me.EventType().Should().Be(EventType.MemberLeave, "EventType() should return MemberLeave");
        me.ToString().Should().Be("member-leave", "ToString() should return 'member-leave'");

        // Test MemberFailed
        me.Type = EventType.MemberFailed;
        me.EventType().Should().Be(EventType.MemberFailed, "EventType() should return MemberFailed");
        me.ToString().Should().Be("member-failed", "ToString() should return 'member-failed'");

        // Test MemberUpdate
        me.Type = EventType.MemberUpdate;
        me.EventType().Should().Be(EventType.MemberUpdate, "EventType() should return MemberUpdate");
        me.ToString().Should().Be("member-update", "ToString() should return 'member-update'");

        // Test MemberReap
        me.Type = EventType.MemberReap;
        me.EventType().Should().Be(EventType.MemberReap, "EventType() should return MemberReap");
        me.ToString().Should().Be("member-reap", "ToString() should return 'member-reap'");
    }

    [Fact]
    public void MemberEvent_InvalidEventType_ShouldThrowException()
    {
        // Arrange - Set to User event type (invalid for MemberEvent)
        var me = new MemberEvent
        {
            Type = EventType.User,
            Members = new List<Member>()
        };

        // Act & Assert - Should throw exception (matches Go's panic)
        Action act = () => me.ToString();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("unknown event type: 5");
    }

    [Fact]
    public void UserEvent_ShouldHaveCorrectProperties()
    {
        // Arrange
        var ue = new UserEvent
        {
            Name = "test",
            Payload = "foobar"u8.ToArray()
        };

        // Act & Assert
        ue.EventType().Should().Be(EventType.User, "EventType() should return User");
        ue.ToString().Should().Be("user-event: test", "ToString() should return 'user-event: test'");
    }

    [Fact]
    public void Query_ShouldHaveCorrectProperties()
    {
        // Arrange
        var q = new Query
        {
            LTime = 42,
            Name = "update",
            Payload = "abcd1234"u8.ToArray()
        };

        // Act & Assert
        q.EventType().Should().Be(EventType.Query, "EventType() should return Query");
        q.ToString().Should().Be("query: update", "ToString() should return 'query: update'");
    }

    [Fact]
    public void EventType_String_AllTypes_ShouldReturnCorrectStrings()
    {
        // Arrange - All event types
        var events = new[]
        {
            EventType.MemberJoin,
            EventType.MemberLeave,
            EventType.MemberFailed,
            EventType.MemberUpdate,
            EventType.MemberReap,
            EventType.User,
            EventType.Query
        };

        var expected = new[]
        {
            "member-join",
            "member-leave",
            "member-failed",
            "member-update",
            "member-reap",
            "user",
            "query"
        };

        // Act & Assert
        for (int idx = 0; idx < events.Length; idx++)
        {
            events[idx].String().Should().Be(expected[idx],
                $"EventType {events[idx]} should have string representation '{expected[idx]}'");
        }
    }

    [Fact]
    public void EventType_String_UnknownType_ShouldThrowException()
    {
        // Arrange - Invalid event type
        var other = (EventType)100;

        // Act & Assert - Should throw exception (matches Go's panic)
        Action act = () => other.String();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("unknown event type: 100");
    }

    [Fact]
    public void MemberEvent_WithMembers_ShouldStoreMembers()
    {
        // Arrange
        var members = new List<Member>
        {
            new Member { Name = "node1", Addr = System.Net.IPAddress.Parse("127.0.0.1") },
            new Member { Name = "node2", Addr = System.Net.IPAddress.Parse("127.0.0.2") }
        };

        var me = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = members
        };

        // Act & Assert
        me.Members.Should().HaveCount(2);
        me.Members[0].Name.Should().Be("node1");
        me.Members[1].Name.Should().Be("node2");
    }

    [Fact]
    public void UserEvent_WithLTime_ShouldStoreLamportTime()
    {
        // Arrange
        var ue = new UserEvent
        {
            LTime = 12345,
            Name = "deployment",
            Payload = "version-1.0"u8.ToArray(),
            Coalesce = true
        };

        // Act & Assert
        ue.LTime.Should().Be((LamportTime)12345);
        ue.Name.Should().Be("deployment");
        ue.Payload.Should().Equal("version-1.0"u8.ToArray());
        ue.Coalesce.Should().BeTrue();
    }

    [Fact]
    public void Query_SourceNode_ShouldReturnSourceNodeName()
    {
        // Arrange
        var q = new Query
        {
            LTime = 100,
            Name = "ping",
            Payload = Array.Empty<byte>(),
            SourceNodeName = "initiator-node"
        };

        // Act
        var source = q.SourceNode();

        // Assert
        source.Should().Be("initiator-node");
    }

    [Fact]
    public void Query_GetDeadline_ShouldReturnDeadline()
    {
        // Arrange
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var q = new Query
        {
            LTime = 200,
            Name = "health-check",
            Payload = Array.Empty<byte>(),
            Deadline = deadline
        };

        // Act
        var result = q.GetDeadline();

        // Assert
        result.Should().Be(deadline);
    }

    [Fact]
    public void MemberEvent_EmptyMembers_ShouldBeValid()
    {
        // Arrange
        var me = new MemberEvent
        {
            Type = EventType.MemberLeave,
            Members = new List<Member>()
        };

        // Act & Assert
        me.Members.Should().BeEmpty();
        me.ToString().Should().Be("member-leave");
    }

    [Fact]
    public void UserEvent_EmptyPayload_ShouldBeValid()
    {
        // Arrange
        var ue = new UserEvent
        {
            Name = "signal",
            Payload = Array.Empty<byte>(),
            Coalesce = false
        };

        // Act & Assert
        ue.Payload.Should().BeEmpty();
        ue.ToString().Should().Be("user-event: signal");
    }

    [Fact]
    public void Query_EmptyPayload_ShouldBeValid()
    {
        // Arrange
        var q = new Query
        {
            Name = "poll",
            Payload = Array.Empty<byte>()
        };

        // Act & Assert
        q.Payload.Should().BeEmpty();
        q.ToString().Should().Be("query: poll");
    }

    [Fact]
    public void Event_Interface_AllEventTypes_ShouldImplementInterface()
    {
        // Arrange & Act
        IEvent memberEvent = new MemberEvent { Type = EventType.MemberJoin };
        IEvent userEvent = new UserEvent { Name = "test" };
        IEvent queryEvent = new Query { Name = "test-query" };

        // Assert - All should implement Event interface
        memberEvent.Should().BeAssignableTo<IEvent>();
        userEvent.Should().BeAssignableTo<IEvent>();
        queryEvent.Should().BeAssignableTo<IEvent>();

        // All should have EventType() method
        memberEvent.EventType().Should().Be(EventType.MemberJoin);
        userEvent.EventType().Should().Be(EventType.User);
        queryEvent.EventType().Should().Be(EventType.Query);
    }

    [Fact]
    public void MemberEvent_MultipleTypes_ShouldSwitchCorrectly()
    {
        // Arrange
        var me = new MemberEvent { Members = new List<Member>() };

        // Test each type
        var typeTests = new Dictionary<EventType, string>
        {
            { EventType.MemberJoin, "member-join" },
            { EventType.MemberLeave, "member-leave" },
            { EventType.MemberFailed, "member-failed" },
            { EventType.MemberUpdate, "member-update" },
            { EventType.MemberReap, "member-reap" }
        };

        foreach (var (eventType, expectedString) in typeTests)
        {
            // Act
            me.Type = eventType;

            // Assert
            me.ToString().Should().Be(expectedString,
                $"Type {eventType} should produce string '{expectedString}'");
        }
    }

    [Fact]
    public void Query_InternalFields_ShouldBeAccessible()
    {
        // Arrange
        var q = new Query
        {
            LTime = 999,
            Name = "internal-test",
            Payload = "data"u8.ToArray(),
            Id = 12345,
            Addr = new byte[] { 192, 168, 1, 100 },
            Port = 7946,
            SourceNodeName = "source",
            Deadline = DateTime.UtcNow.AddMinutes(5),
            RelayFactor = 3
        };

        // Act & Assert - Internal fields should be set
        q.Id.Should().Be(12345u);
        q.Addr.Should().Equal(new byte[] { 192, 168, 1, 100 });
        q.Port.Should().Be(7946);
        q.SourceNode().Should().Be("source");
        q.RelayFactor.Should().Be(3);
        q.GetDeadline().Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void UserEvent_Coalesce_ShouldToggle()
    {
        // Arrange
        var ue = new UserEvent
        {
            Name = "test",
            Payload = Array.Empty<byte>(),
            Coalesce = false
        };

        // Act & Assert - Initial state
        ue.Coalesce.Should().BeFalse();

        // Act - Toggle
        ue.Coalesce = true;

        // Assert - Should be true
        ue.Coalesce.Should().BeTrue();
    }
}
