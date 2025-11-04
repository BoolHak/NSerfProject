// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/merge_delegate_test.go

using FluentAssertions;
using NSerf.Memberlist.State;
using NSerf.Serf;
using System.Net;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for the Serf Merge Delegate implementation.
/// Tests node-to-member conversion and validation logic.
/// </summary>
public class MergeDelegateTest
{
    [Fact]
    public void ValidateMemberInfo_WithInvalidNameChars_ShouldReturnError()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            ValidateNodeNames = true
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = "space not allowed",  // Invalid: contains space
            Addr = IPAddress.Parse("1.2.3.4"),
            Meta = Array.Empty<byte>()
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("invalid characters");
    }

    [Fact]
    public void ValidateMemberInfo_WithInvalidNameCharsButValidationDisabled_ShouldReturnNull()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            ValidateNodeNames = false  // Validation disabled
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = "space not allowed",
            Addr = IPAddress.Parse("1.2.3.4"),
            Meta = Array.Empty<byte>()
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().BeNull("validation is disabled");
    }

    [Fact]
    public void ValidateMemberInfo_WithNameTooLong_ShouldReturnError()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            ValidateNodeNames = true
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = new string('a', 132),  // 132 characters - too long
            Addr = IPAddress.Parse("::1"),
            Meta = Array.Empty<byte>()
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("132 characters");
    }

    [Fact]
    public void ValidateMemberInfo_WithNameTooLongButValidationDisabled_ShouldReturnNull()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>(),
            ValidateNodeNames = false
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = new string('a', 132),
            Addr = IPAddress.Parse("::1"),
            Meta = Array.Empty<byte>()
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().BeNull("validation is disabled");
    }

    [Fact]
    public void ValidateMemberInfo_WithInvalidIPLength_ShouldReturnError()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        // Create a mock node with invalid IP (AddressFamily will reject non-4/16 byte IPs at Node creation)
        // So this test verifies the validation logic exists
        var node = new Node
        {
            Name = "test",
            Addr = IPAddress.Parse("127.0.0.1"),  // Valid for now
            Meta = Array.Empty<byte>()
        };

        // Manually create invalid byte array
        var invalidAddr = new byte[] { 1, 2 };  // Only 2 bytes - invalid

        // Act - Test the internal validation logic directly
        var result = MergeDelegate.ValidateIpLength(invalidAddr);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("IP byte length is invalid");
    }

    [Fact]
    public void ValidateMemberInfo_WithMetaTooLong_ShouldReturnError()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = "test",
            Addr = IPAddress.Parse("::1"),
            Meta = new byte[513]  // 513 bytes - exceeds limit of 512
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().NotBeNull();
        result.Should().Contain("exceeds limit");
    }

    [Fact]
    public void ValidateMemberInfo_WithValidIPv4_ShouldReturnNull()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = "test",
            Addr = IPAddress.Parse("1.1.1.1"),
            Meta = Array.Empty<byte>()
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().BeNull("IPv4 address is valid");
    }

    [Fact]
    public void ValidateMemberInfo_WithValidIPv6_ShouldReturnNull()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = "test",
            Addr = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334"),
            Meta = Array.Empty<byte>()
        };

        // Act
        var result = mergeDelegate.ValidateMemberInfo(node);

        // Assert
        result.Should().BeNull("IPv6 address is valid");
    }

    [Fact]
    public void NodeToMember_ShouldConvertNodeToMember()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);
        var mergeDelegate = new MergeDelegate(serf);

        var node = new Node
        {
            Name = "remote-node",
            Addr = IPAddress.Parse("192.168.1.100"),
            Port = 7946,
            Meta = Array.Empty<byte>(),
            PMin = 1,
            PMax = 5,
            PCur = 3,
            DMin = 1,
            DMax = 3,
            DCur = 2
        };

        // Act
        var (member, error) = mergeDelegate.NodeToMember(node);

        // Assert
        error.Should().BeNull();
        member.Should().NotBeNull();
        member!.Name.Should().Be("remote-node");
        member.Addr.Should().Be(IPAddress.Parse("192.168.1.100"));
        member.Port.Should().Be(7946);
        member.ProtocolMin.Should().Be(1);
        member.ProtocolMax.Should().Be(5);
        member.ProtocolCur.Should().Be(3);
        member.DelegateMin.Should().Be(1);
        member.DelegateMax.Should().Be(3);
        member.DelegateCur.Should().Be(2);

        // TODO: Phase 9 - Verify member status mapping and tag decoding
    }

    [Fact]
    public void Constructor_WithNullSerf_ShouldThrow()
    {
        // Act & Assert
        var act = () => new MergeDelegate(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("serf");
    }
}
