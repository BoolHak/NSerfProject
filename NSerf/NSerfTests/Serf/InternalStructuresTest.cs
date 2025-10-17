// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Serf;
using System.Net;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for internal Serf data structures.
/// </summary>
public class InternalStructuresTest
{
    [Fact]
    public void MemberState_DefaultConstructor_ShouldInitialize()
    {
        // Arrange & Act
        var memberState = new MemberState();

        // Assert
        memberState.Member.Should().NotBeNull();
        memberState.StatusLTime.Should().Be(0);
        memberState.LeaveTime.Should().Be(default(DateTime));
    }

    [Fact]
    public void MemberState_WithProperties_ShouldStoreCorrectly()
    {
        // Arrange
        var member = new Member
        {
            Name = "test-node",
            Status = MemberStatus.Failed
        };
        var lTime = new LamportTime(42);
        var leaveTime = DateTime.UtcNow;

        // Act
        var memberState = new MemberState
        {
            Member = member,
            StatusLTime = lTime,
            LeaveTime = leaveTime
        };

        // Assert
        memberState.Member.Should().BeSameAs(member);
        memberState.StatusLTime.Should().Be(42);
        memberState.LeaveTime.Should().Be(leaveTime);
    }

    [Fact]
    public void MemberState_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var memberState = new MemberState
        {
            Member = new Member { Name = "node1", Status = MemberStatus.Alive },
            StatusLTime = 100,
            LeaveTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var result = memberState.ToString();

        // Assert
        result.Should().Contain("node1");
        result.Should().Contain("alive");
        result.Should().Contain("100");
    }

    [Fact]
    public void NodeIntent_AllProperties_ShouldStoreCorrectly()
    {
        // Arrange
        var wallTime = DateTime.UtcNow;
        var lTime = new LamportTime(50);

        // Act
        var intent = new NodeIntent
        {
            Type = MessageType.Join,
            WallTime = wallTime,
            LTime = lTime
        };

        // Assert
        intent.Type.Should().Be(MessageType.Join);
        intent.WallTime.Should().Be(wallTime);
        intent.LTime.Should().Be(50);
    }

    [Fact]
    public void NodeIntent_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var intent = new NodeIntent
        {
            Type = MessageType.Leave,
            LTime = 200,
            WallTime = DateTime.UtcNow
        };

        // Act
        var result = intent.ToString();

        // Assert
        result.Should().Contain("Leave");
        result.Should().Contain("200");
    }

    [Fact]
    public void MessageType_AllValues_ShouldBeUnique()
    {
        // Arrange & Act
        var values = Enum.GetValues<MessageType>();

        // Assert
        values.Should().OnlyHaveUniqueItems();
        values.Should().Contain(MessageType.Join);
        values.Should().Contain(MessageType.Leave);
        values.Should().Contain(MessageType.UserEvent);
        values.Should().Contain(MessageType.Query);
    }

    [Fact]
    public void UserEventData_Equals_WithSameData_ShouldReturnTrue()
    {
        // Arrange
        var event1 = new UserEventData
        {
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3 }
        };
        var event2 = new UserEventData
        {
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        var result = event1.Equals(event2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void UserEventData_Equals_WithDifferentName_ShouldReturnFalse()
    {
        // Arrange
        var event1 = new UserEventData
        {
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3 }
        };
        var event2 = new UserEventData
        {
            Name = "restart",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        var result = event1.Equals(event2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void UserEventData_Equals_WithDifferentPayload_ShouldReturnFalse()
    {
        // Arrange
        var event1 = new UserEventData
        {
            Name = "deploy",
            Payload = new byte[] { 1, 2, 3 }
        };
        var event2 = new UserEventData
        {
            Name = "deploy",
            Payload = new byte[] { 1, 2, 4 }
        };

        // Act
        var result = event1.Equals(event2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void UserEventData_Equals_WithNull_ShouldReturnFalse()
    {
        // Arrange
        var event1 = new UserEventData { Name = "test" };

        // Act
        var result = event1.Equals(null);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void UserEventCollection_ShouldStoreMultipleEvents()
    {
        // Arrange
        var collection = new UserEventCollection
        {
            LTime = 100,
            Events = new List<UserEventData>
            {
                new() { Name = "event1", Payload = new byte[] { 1 } },
                new() { Name = "event2", Payload = new byte[] { 2 } }
            }
        };

        // Act & Assert
        collection.LTime.Should().Be(100);
        collection.Events.Should().HaveCount(2);
        collection.Events[0].Name.Should().Be("event1");
        collection.Events[1].Name.Should().Be("event2");
    }

    [Fact]
    public void UserEventCollection_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var collection = new UserEventCollection
        {
            LTime = 50,
            Events = new List<UserEventData>
            {
                new() { Name = "event1" },
                new() { Name = "event2" },
                new() { Name = "event3" }
            }
        };

        // Act
        var result = collection.ToString();

        // Assert
        result.Should().Contain("50");
        result.Should().Contain("3");
    }

    [Fact]
    public void QueryCollection_ShouldStoreMultipleQueryIDs()
    {
        // Arrange
        var collection = new QueryCollection
        {
            LTime = 75,
            QueryIDs = new List<uint> { 1, 2, 3, 4, 5 }
        };

        // Act & Assert
        collection.LTime.Should().Be(75);
        collection.QueryIDs.Should().HaveCount(5);
        collection.QueryIDs.Should().Contain(new uint[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void QueryCollection_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var collection = new QueryCollection
        {
            LTime = 123,
            QueryIDs = new List<uint> { 10, 20 }
        };

        // Act
        var result = collection.ToString();

        // Assert
        result.Should().Contain("123");
        result.Should().Contain("2");
    }

    [Fact]
    public void SerfState_AllValues_ShouldHaveCorrectStringRepresentation()
    {
        // Arrange & Act & Assert
        SerfState.SerfAlive.ToStateString().Should().Be("alive");
        SerfState.SerfLeaving.ToStateString().Should().Be("leaving");
        SerfState.SerfLeft.ToStateString().Should().Be("left");
        SerfState.SerfShutdown.ToStateString().Should().Be("shutdown");
    }

    [Fact]
    public void SerfState_InvalidValue_ShouldReturnUnknown()
    {
        // Arrange
        var invalidState = (SerfState)99;

        // Act
        var result = invalidState.ToStateString();

        // Assert
        result.Should().Be("unknown");
    }

    [Fact]
    public void UserEventData_EmptyPayload_ShouldBeValid()
    {
        // Arrange & Act
        var event1 = new UserEventData
        {
            Name = "test",
            Payload = Array.Empty<byte>()
        };
        var event2 = new UserEventData
        {
            Name = "test",
            Payload = Array.Empty<byte>()
        };

        // Assert
        event1.Equals(event2).Should().BeTrue();
    }

    [Fact]
    public void MemberState_CanTrackFailedMember()
    {
        // Arrange
        var failTime = DateTime.UtcNow;
        var memberState = new MemberState
        {
            Member = new Member
            {
                Name = "failed-node",
                Status = MemberStatus.Failed
            },
            StatusLTime = 500,
            LeaveTime = failTime
        };

        // Act & Assert
        memberState.Member.Status.Should().Be(MemberStatus.Failed);
        memberState.StatusLTime.Should().Be(500);
        memberState.LeaveTime.Should().Be(failTime);
    }

    [Fact]
    public void NodeIntent_CanDistinguishJoinFromLeave()
    {
        // Arrange
        var joinIntent = new NodeIntent { Type = MessageType.Join };
        var leaveIntent = new NodeIntent { Type = MessageType.Leave };

        // Act & Assert
        joinIntent.Type.Should().Be(MessageType.Join);
        leaveIntent.Type.Should().Be(MessageType.Leave);
        joinIntent.Type.Should().NotBe(leaveIntent.Type);
    }
}
