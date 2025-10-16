// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Serf;
using System.Net;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Member class and related functionality.
/// </summary>
public class MemberTest
{
    [Fact]
    public void Member_DefaultConstructor_ShouldHaveEmptyValues()
    {
        // Arrange & Act
        var member = new Member();

        // Assert
        member.Name.Should().BeEmpty();
        member.Addr.Should().Be(IPAddress.None);
        member.Port.Should().Be(0);
        member.Tags.Should().NotBeNull().And.BeEmpty();
        member.Status.Should().Be(MemberStatus.None);
    }

    [Fact]
    public void Member_WithProperties_ShouldStoreCorrectly()
    {
        // Arrange & Act
        var member = new Member
        {
            Name = "test-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946,
            Status = MemberStatus.Alive,
            Tags = new Dictionary<string, string>
            {
                ["role"] = "web",
                ["datacenter"] = "us-east-1"
            },
            ProtocolMin = 2,
            ProtocolMax = 5,
            ProtocolCur = 5,
            DelegateMin = 2,
            DelegateMax = 5,
            DelegateCur = 5
        };

        // Assert
        member.Name.Should().Be("test-node");
        member.Addr.Should().Be(IPAddress.Parse("192.168.1.100"));
        member.Port.Should().Be(7946);
        member.Status.Should().Be(MemberStatus.Alive);
        member.Tags.Should().HaveCount(2);
        member.Tags["role"].Should().Be("web");
        member.Tags["datacenter"].Should().Be("us-east-1");
        member.ProtocolMin.Should().Be(2);
        member.ProtocolMax.Should().Be(5);
        member.ProtocolCur.Should().Be(5);
        member.DelegateMin.Should().Be(2);
        member.DelegateMax.Should().Be(5);
        member.DelegateCur.Should().Be(5);
    }

    [Fact]
    public void Member_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var member = new Member
        {
            Name = "node1",
            Addr = IPAddress.Parse("10.0.0.1"),
            Port = 8000,
            Status = MemberStatus.Alive
        };

        // Act
        var result = member.ToString();

        // Assert
        result.Should().Be("node1 (10.0.0.1:8000) - alive");
    }

    [Fact]
    public void Member_Clone_ShouldCreateDeepCopy()
    {
        // Arrange
        var original = new Member
        {
            Name = "original",
            Addr = IPAddress.Parse("192.168.1.1"),
            Port = 5000,
            Status = MemberStatus.Alive,
            Tags = new Dictionary<string, string> { ["key"] = "value" },
            ProtocolMin = 1,
            ProtocolMax = 3,
            ProtocolCur = 2,
            DelegateMin = 1,
            DelegateMax = 3,
            DelegateCur = 2
        };

        // Act
        var clone = original.Clone();

        // Assert - values should match
        clone.Name.Should().Be(original.Name);
        clone.Addr.Should().Be(original.Addr);
        clone.Port.Should().Be(original.Port);
        clone.Status.Should().Be(original.Status);
        clone.Tags.Should().Equal(original.Tags);
        clone.ProtocolMin.Should().Be(original.ProtocolMin);
        clone.ProtocolMax.Should().Be(original.ProtocolMax);
        clone.ProtocolCur.Should().Be(original.ProtocolCur);
        clone.DelegateMin.Should().Be(original.DelegateMin);
        clone.DelegateMax.Should().Be(original.DelegateMax);
        clone.DelegateCur.Should().Be(original.DelegateCur);

        // Assert - tags should be a separate instance
        clone.Should().NotBeSameAs(original);
        clone.Tags.Should().NotBeSameAs(original.Tags);
        
        // Modify clone's tags
        clone.Tags["new"] = "tag";
        original.Tags.Should().NotContainKey("new");
    }

    [Fact]
    public void MemberStatus_AllValues_ShouldHaveCorrectStringRepresentation()
    {
        // Arrange & Act & Assert
        MemberStatus.None.ToStatusString().Should().Be("none");
        MemberStatus.Alive.ToStatusString().Should().Be("alive");
        MemberStatus.Leaving.ToStatusString().Should().Be("leaving");
        MemberStatus.Left.ToStatusString().Should().Be("left");
        MemberStatus.Failed.ToStatusString().Should().Be("failed");
    }

    [Fact]
    public void MemberStatus_InvalidValue_ShouldThrowException()
    {
        // Arrange
        var invalidStatus = (MemberStatus)99;

        // Act
        var act = () => invalidStatus.ToStatusString();

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*unknown MemberStatus: 99*");
    }

    [Fact]
    public void Member_WithEmptyTags_ShouldNotBeNull()
    {
        // Arrange & Act
        var member = new Member
        {
            Name = "test",
            Tags = new Dictionary<string, string>()
        };

        // Assert
        member.Tags.Should().NotBeNull();
        member.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Member_ModifyTags_ShouldNotAffectOtherInstances()
    {
        // Arrange
        var member1 = new Member
        {
            Name = "member1",
            Tags = new Dictionary<string, string> { ["key1"] = "value1" }
        };
        var member2 = new Member
        {
            Name = "member2",
            Tags = new Dictionary<string, string> { ["key2"] = "value2" }
        };

        // Act
        member1.Tags["key3"] = "value3";

        // Assert
        member1.Tags.Should().HaveCount(2);
        member2.Tags.Should().HaveCount(1);
        member2.Tags.Should().NotContainKey("key3");
    }

    [Fact]
    public void Member_ProtocolVersions_ShouldAcceptByteRange()
    {
        // Arrange & Act
        var member = new Member
        {
            Name = "test",
            ProtocolMin = 0,
            ProtocolMax = 255,
            ProtocolCur = 128,
            DelegateMin = 0,
            DelegateMax = 255,
            DelegateCur = 128
        };

        // Assert
        member.ProtocolMin.Should().Be(0);
        member.ProtocolMax.Should().Be(255);
        member.ProtocolCur.Should().Be(128);
        member.DelegateMin.Should().Be(0);
        member.DelegateMax.Should().Be(255);
        member.DelegateCur.Should().Be(128);
    }
}
