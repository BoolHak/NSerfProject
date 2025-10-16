// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Serf;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for tag encoding and decoding functionality.
/// </summary>
public class TagEncoderTest
{
    [Fact]
    public void EncodeTags_WithProtocolVersion3_ShouldIncludeMagicByte()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["role"] = "web",
            ["datacenter"] = "us-east-1"
        };

        // Act
        var encoded = TagEncoder.EncodeTags(tags, protocolVersion: 3);

        // Assert
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be(TagEncoder.TagMagicByte, "first byte should be magic byte");
    }

    [Fact]
    public void EncodeTags_WithProtocolVersion2_ShouldUseRawRole()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["role"] = "database",
            ["other"] = "ignored"  // Should be ignored in protocol v2
        };

        // Act
        var encoded = TagEncoder.EncodeTags(tags, protocolVersion: 2);

        // Assert
        var decoded = System.Text.Encoding.UTF8.GetString(encoded);
        decoded.Should().Be("database", "protocol v2 should only encode role as raw string");
    }

    [Fact]
    public void EncodeTags_WithProtocolVersion2_NoRole_ShouldReturnEmpty()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["datacenter"] = "us-west"
        };

        // Act
        var encoded = TagEncoder.EncodeTags(tags, protocolVersion: 2);

        // Assert
        encoded.Should().BeEmpty("protocol v2 with no role should return empty");
    }

    [Fact]
    public void DecodeTags_WithMagicByte_ShouldDecodeCorrectly()
    {
        // Arrange
        var originalTags = new Dictionary<string, string>
        {
            ["role"] = "cache",
            ["region"] = "eu-west-1",
            ["version"] = "1.2.3"
        };
        var encoded = TagEncoder.EncodeTags(originalTags, protocolVersion: 3);

        // Act
        var decoded = TagEncoder.DecodeTags(encoded);

        // Assert
        decoded.Should().HaveCount(3);
        decoded["role"].Should().Be("cache");
        decoded["region"].Should().Be("eu-west-1");
        decoded["version"].Should().Be("1.2.3");
    }

    [Fact]
    public void DecodeTags_WithoutMagicByte_ShouldTreatAsRole()
    {
        // Arrange - raw string without magic byte (backwards compatibility)
        var roleString = "webserver";
        var encoded = System.Text.Encoding.UTF8.GetBytes(roleString);

        // Act
        var decoded = TagEncoder.DecodeTags(encoded);

        // Assert
        decoded.Should().HaveCount(1);
        decoded["role"].Should().Be("webserver");
    }

    [Fact]
    public void DecodeTags_WithEmptyBuffer_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var encoded = Array.Empty<byte>();

        // Act
        var decoded = TagEncoder.DecodeTags(encoded);

        // Assert
        decoded.Should().NotBeNull();
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void DecodeTags_WithNull_ShouldReturnEmptyDictionary()
    {
        // Arrange
        byte[]? encoded = null;

        // Act
        var decoded = TagEncoder.DecodeTags(encoded!);

        // Assert
        decoded.Should().NotBeNull();
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void EncodeDecode_RoundTrip_ShouldPreserveTags()
    {
        // Arrange
        var originalTags = new Dictionary<string, string>
        {
            ["role"] = "api",
            ["datacenter"] = "us-central",
            ["env"] = "production",
            ["version"] = "2.0.0"
        };

        // Act
        var encoded = TagEncoder.EncodeTags(originalTags, protocolVersion: 5);
        var decoded = TagEncoder.DecodeTags(encoded);

        // Assert
        decoded.Should().Equal(originalTags);
    }

    [Fact]
    public void EncodeTags_WithEmptyDictionary_ShouldEncodeCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string>();

        // Act
        var encoded = TagEncoder.EncodeTags(tags, protocolVersion: 3);

        // Assert
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be(TagEncoder.TagMagicByte);
        
        var decoded = TagEncoder.DecodeTags(encoded);
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void EncodeTags_WithNullDictionary_ShouldHandleGracefully()
    {
        // Arrange
        Dictionary<string, string>? tags = null;

        // Act
        var encoded = TagEncoder.EncodeTags(tags!, protocolVersion: 3);

        // Assert
        encoded.Should().NotBeEmpty();
        encoded[0].Should().Be(TagEncoder.TagMagicByte);
    }

    [Fact]
    public void IsTagEncoded_WithMagicByte_ShouldReturnTrue()
    {
        // Arrange
        var tags = new Dictionary<string, string> { ["key"] = "value" };
        var encoded = TagEncoder.EncodeTags(tags, protocolVersion: 3);

        // Act
        var result = TagEncoder.IsTagEncoded(encoded);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsTagEncoded_WithoutMagicByte_ShouldReturnFalse()
    {
        // Arrange
        var encoded = System.Text.Encoding.UTF8.GetBytes("plain role string");

        // Act
        var result = TagEncoder.IsTagEncoded(encoded);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsTagEncoded_WithEmptyBuffer_ShouldReturnFalse()
    {
        // Arrange
        var encoded = Array.Empty<byte>();

        // Act
        var result = TagEncoder.IsTagEncoded(encoded);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EncodeTags_WithSpecialCharacters_ShouldEncodeCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string>
        {
            ["name"] = "node-1",
            ["path"] = "/var/lib/data",
            ["unicode"] = "Hello 世界 🌍"
        };

        // Act
        var encoded = TagEncoder.EncodeTags(tags, protocolVersion: 3);
        var decoded = TagEncoder.DecodeTags(encoded);

        // Assert
        decoded.Should().Equal(tags);
        decoded["unicode"].Should().Be("Hello 世界 🌍");
    }

    [Fact]
    public void DecodeTags_WithInvalidMessagePack_ShouldReturnEmpty()
    {
        // Arrange - create buffer with magic byte but invalid MessagePack data
        var invalidBuffer = new byte[] { TagEncoder.TagMagicByte, 0xFF, 0xFF, 0xFF };

        // Act
        var decoded = TagEncoder.DecodeTags(invalidBuffer);

        // Assert
        decoded.Should().NotBeNull();
        decoded.Should().BeEmpty("invalid MessagePack should return empty dictionary");
    }

    [Fact]
    public void EncodeTags_MultipleProtocolVersions_ShouldBehaveCorrectly()
    {
        // Arrange
        var tags = new Dictionary<string, string> { ["role"] = "worker" };

        // Act
        var v2Encoded = TagEncoder.EncodeTags(tags, protocolVersion: 2);
        var v3Encoded = TagEncoder.EncodeTags(tags, protocolVersion: 3);
        var v5Encoded = TagEncoder.EncodeTags(tags, protocolVersion: 5);

        // Assert
        v2Encoded[0].Should().NotBe(TagEncoder.TagMagicByte);
        v3Encoded[0].Should().Be(TagEncoder.TagMagicByte);
        v5Encoded[0].Should().Be(TagEncoder.TagMagicByte);
    }

    [Fact]
    public void TagMagicByte_ShouldBe255()
    {
        // Assert
        TagEncoder.TagMagicByte.Should().Be(255);
    }

    [Fact]
    public void MinTagProtocolVersion_ShouldBe3()
    {
        // Assert
        TagEncoder.MinTagProtocolVersion.Should().Be(3);
    }
}
