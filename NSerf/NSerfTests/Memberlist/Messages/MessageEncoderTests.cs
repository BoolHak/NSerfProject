// Ported from: github.com/hashicorp/memberlist/util_test.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Messages;

namespace NSerfTests.Memberlist.Messages;

public class MessageEncoderTests
{
    [Fact]
    public void Encode_Decode_ShouldRoundTrip()
    {
        // Arrange
        var original = new PingMessage { SeqNo = 100, Node = "test-node" };
        
        // Act - Encode
        var encoded = MessageEncoder.Encode(MessageType.Ping, original);
        
        // Assert - Should have message type byte prefix
        encoded[0].Should().Be((byte)MessageType.Ping);
        
        // Act - Decode
        var decoded = MessageEncoder.Decode<PingMessage>(encoded.AsSpan(1));
        
        // Assert - Should match original
        decoded.SeqNo.Should().Be(100);
        decoded.Node.Should().Be("test-node");
    }
    
    [Fact]
    public void MakeCompoundMessage_ShouldBundleMessages()
    {
        // Arrange - Create 3 identical messages
        var msg = new PingMessage { SeqNo = 100 };
        var encoded = MessageEncoder.Encode(MessageType.Ping, msg);
        var messages = new[] { encoded, encoded, encoded };
        
        // Act
        var compound = MessageEncoder.MakeCompoundMessage(messages);
        
        // Assert - Should have correct format: 
        // [CompoundMsg byte][num messages][length1][length2][length3][msg1][msg2][msg3]
        compound[0].Should().Be((byte)MessageType.Compound);
        compound[1].Should().Be(3); // Number of messages
        
        var expectedLen = 1 +  // compound type byte
                         1 +  // num messages
                         3 * 2 + // 3 message lengths (ushort each = 2 bytes)
                         3 * encoded.Length; // 3 messages
        compound.Length.Should().Be(expectedLen);
    }
    
    [Fact]
    public void DecodeCompoundMessage_ShouldExtractMessages()
    {
        // Arrange
        var msg = new PingMessage { SeqNo = 100 };
        var encoded = MessageEncoder.Encode(MessageType.Ping, msg);
        var messages = new[] { encoded, encoded, encoded };
        var compound = MessageEncoder.MakeCompoundMessage(messages);
        
        // Act - Skip the compound type byte
        var (truncated, parts) = MessageEncoder.DecodeCompoundMessage(compound.AsSpan(1));
        
        // Assert
        truncated.Should().Be(0, "should not truncate with valid data");
        parts.Should().HaveCount(3);
        parts.Should().AllSatisfy(p => p.Length.Should().Be(encoded.Length));
    }
    
    [Fact]
    public void DecodeCompoundMessage_ShouldHandleTruncation()
    {
        // Arrange - Create compound message but truncate it
        var msg = new PingMessage { SeqNo = 100 };
        var encoded = MessageEncoder.Encode(MessageType.Ping, msg);
        var messages = new[] { encoded, encoded, encoded };
        var compound = MessageEncoder.MakeCompoundMessage(messages);
        
        // Calculate safe truncation point: header + lengths + 2 complete messages
        var truncateAt = Math.Min(compound.Length - encoded.Length - 1, 38);
        
        // Act - Truncate to only fit 2 messages
        var truncatedCompound = compound.AsSpan(1, truncateAt); // Skip type byte, truncate
        var (truncated, parts) = MessageEncoder.DecodeCompoundMessage(truncatedCompound);
        
        // Assert
        truncated.Should().BeGreaterThan(0, "should report truncation");
        parts.Count.Should().BeLessThan(3, "should not extract all messages");
    }
    
    [Fact]
    public void DecodeCompoundMessage_ShouldErrorOnInvalidFormat()
    {
        // Arrange - Invalid compound message (claims 128 messages but no length data)
        var invalidCompound = new byte[] { 0x80 };
        
        // Act
        Action act = () => MessageEncoder.DecodeCompoundMessage(invalidCompound);
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*truncated len slice*");
    }
    
    [Fact]
    public void MakeCompoundMessages_ShouldSplitLargeMessageLists()
    {
        // Arrange - Create more than 255 messages (the limit per compound)
        var messages = new List<byte[]>();
        var singleMsg = MessageEncoder.Encode(MessageType.Ping, new PingMessage { SeqNo = 1 });
        
        for (int i = 0; i < 300; i++)
        {
            messages.Add(singleMsg);
        }
        
        // Act
        var compounds = MessageEncoder.MakeCompoundMessages(messages);
        
        // Assert - Should split into 2 compound messages
        compounds.Should().HaveCount(2);
        
        // First compound should have 255 messages
        var (trunc1, parts1) = MessageEncoder.DecodeCompoundMessage(compounds[0].AsSpan(1));
        trunc1.Should().Be(0);
        parts1.Should().HaveCount(255);
        
        // Second compound should have remaining 45 messages
        var (trunc2, parts2) = MessageEncoder.DecodeCompoundMessage(compounds[1].AsSpan(1));
        trunc2.Should().Be(0);
        parts2.Should().HaveCount(45);
    }
}
