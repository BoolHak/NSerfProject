// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Messages;

namespace NSerfTests.Memberlist;

public class CompoundMessageTests
{
    [Fact]
    public void MakeCompoundMessage_SingleMessage_ShouldEncode()
    {
        // Arrange
        var msg1 = "Hello"u8.ToArray();
        var messages = new List<byte[]> { msg1 };
        
        // Act
        var compound = CompoundMessage.MakeCompoundMessage(messages);
        
        // Assert
        compound.Should().NotBeEmpty();
        compound[0].Should().Be((byte)MessageType.Compound);
        compound[1].Should().Be(1, "should have 1 message");
    }
    
    [Fact]
    public void MakeCompoundMessage_MultipleMessages_ShouldEncode()
    {
        // Arrange
        var msg1 = "Hello"u8.ToArray();
        var msg2 = "World"u8.ToArray();
        var msg3 = "Test"u8.ToArray();
        var messages = new List<byte[]> { msg1, msg2, msg3 };
        
        // Act
        var compound = CompoundMessage.MakeCompoundMessage(messages);
        
        // Assert
        compound.Should().NotBeEmpty();
        compound[0].Should().Be((byte)MessageType.Compound);
        compound[1].Should().Be(3, "should have 3 messages");
    }
    
    [Fact]
    public void DecodeCompoundMessage_ValidMessage_ShouldDecode()
    {
        // Arrange
        var msg1 = "Hello"u8.ToArray();
        var msg2 = "World"u8.ToArray();
        var messages = new List<byte[]> { msg1, msg2 };
        var compound = CompoundMessage.MakeCompoundMessage(messages);
        
        // Skip the compound message type byte for decoding
        var payload = compound[1..];
        
        // Act
        var (truncated, parts) = CompoundMessage.DecodeCompoundMessage(payload);
        
        // Assert
        truncated.Should().Be(0);
        parts.Should().HaveCount(2);
        parts[0].Should().BeEquivalentTo(msg1);
        parts[1].Should().BeEquivalentTo(msg2);
    }
    
    [Fact]
    public void CompoundMessage_RoundTrip_ShouldPreserveMessages()
    {
        // Arrange
        var msg1 = "Test message 1"u8.ToArray();
        var msg2 = "Test message 2"u8.ToArray();
        var msg3 = "Another test message"u8.ToArray();
        var messages = new List<byte[]> { msg1, msg2, msg3 };
        
        // Act - Encode
        var compound = CompoundMessage.MakeCompoundMessage(messages);
        
        // Act - Decode
        var payload = compound[1..]; // Skip message type byte
        var (truncated, decoded) = CompoundMessage.DecodeCompoundMessage(payload);
        
        // Assert
        truncated.Should().Be(0);
        decoded.Should().HaveCount(3);
        decoded[0].Should().BeEquivalentTo(msg1);
        decoded[1].Should().BeEquivalentTo(msg2);
        decoded[2].Should().BeEquivalentTo(msg3);
    }
    
    [Fact]
    public void MakeCompoundMessages_MoreThan255_ShouldSplit()
    {
        // Arrange
        var messages = new List<byte[]>();
        for (int i = 0; i < 300; i++)
        {
            messages.Add(new byte[] { (byte)i });
        }
        
        // Act
        var compounds = CompoundMessage.MakeCompoundMessages(messages);
        
        // Assert
        compounds.Should().HaveCount(2, "should split into 2 compound messages");
    }
    
    [Fact]
    public void DecodeCompoundMessage_TruncatedData_ShouldReportTruncation()
    {
        // Arrange
        var msg1 = "Hello"u8.ToArray();
        var msg2 = "World"u8.ToArray();
        var messages = new List<byte[]> { msg1, msg2 };
        var compound = CompoundMessage.MakeCompoundMessage(messages);
        
        // Truncate the compound message
        var truncated = compound[..(compound.Length - 2)];
        var payload = truncated[1..]; // Skip message type
        
        // Act
        var (truncCount, parts) = CompoundMessage.DecodeCompoundMessage(payload);
        
        // Assert
        truncCount.Should().BeGreaterThan(0, "should detect truncation");
        parts.Should().HaveCountLessThan(2, "should have fewer than expected messages");
    }
}
