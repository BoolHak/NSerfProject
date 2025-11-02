// Ported from: github.com/hashicorp/memberlist/label_test.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using FluentAssertions;
using NSerf.Memberlist;
using Xunit;

namespace NSerfTests.Memberlist;

public class LabelHandlerTests
{
    [Fact]
    public void LabelOverhead_EmptyLabel_ShouldBeZero()
    {
        // Act
        var overhead = LabelHandler.LabelOverhead("");
        
        // Assert
        overhead.Should().Be(0);
    }
    
    [Fact]
    public void LabelOverhead_SingleChar_ShouldBe3()
    {
        // Act
        var overhead = LabelHandler.LabelOverhead("a");
        
        // Assert
        overhead.Should().Be(3); // 1 type byte + 1 length byte + 1 char
    }
    
    [Fact]
    public void LabelOverhead_SevenChars_ShouldBe9()
    {
        // Act
        var overhead = LabelHandler.LabelOverhead("abcdefg");
        
        // Assert
        overhead.Should().Be(9); // 1 type byte + 1 length byte + 7 chars
    }
    
    [Fact]
    public void AddLabelHeaderToPacket_EmptyLabel_ShouldReturnOriginal()
    {
        // Arrange
        var buf = new byte[] { 1, 2, 3, 4 };
        
        // Act
        var result = LabelHandler.AddLabelHeaderToPacket(buf, "");
        
        // Assert
        result.Should().BeSameAs(buf);
    }
    
    [Fact]
    public void AddLabelHeaderToPacket_WithLabel_ShouldPrefixHeader()
    {
        // Arrange
        var buf = new byte[] { 1, 2, 3 };
        var label = "test";
        
        // Act
        var result = LabelHandler.AddLabelHeaderToPacket(buf, label);
        
        // Assert
        result.Should().NotBeNull();
        result.Length.Should().Be(buf.Length + 2 + label.Length);
        result[0].Should().Be((byte)244); // hasLabelMsg
        result[1].Should().Be((byte)label.Length);
    }
    
    [Fact]
    public void AddLabelHeaderToPacket_TooLongLabel_ShouldThrow()
    {
        // Arrange
        var buf = new byte[] { 1, 2, 3 };
        var label = new string('x', 256);
        
        // Act
        Action act = () => LabelHandler.AddLabelHeaderToPacket(buf, label);
        
        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too long*");
    }
    
    [Fact]
    public void RemoveLabelHeaderFromPacket_NoLabel_ShouldReturnOriginal()
    {
        // Arrange
        var buf = new byte[] { 1, 2, 3, 4 };
        
        // Act
        var (newBuf, label) = LabelHandler.RemoveLabelHeaderFromPacket(buf);
        
        // Assert
        newBuf.Should().BeSameAs(buf);
        label.Should().BeEmpty();
    }
    
    [Fact]
    public void RemoveLabelHeaderFromPacket_WithLabel_ShouldExtract()
    {
        // Arrange
        var originalBuf = new byte[] { 10, 20, 30 };
        var label = "test";
        var buf = LabelHandler.AddLabelHeaderToPacket(originalBuf, label);
        
        // Act
        var (newBuf, extractedLabel) = LabelHandler.RemoveLabelHeaderFromPacket(buf);
        
        // Assert
        extractedLabel.Should().Be(label);
        newBuf.Should().BeEquivalentTo(originalBuf);
    }
    
    [Fact]
    public void LabelRoundTrip_ShouldPreserveData()
    {
        // Arrange
        var originalBuf = new byte[] { 1, 2, 3, 4, 5 };
        var label = "myLabel";
        
        // Act - Add label
        var withLabel = LabelHandler.AddLabelHeaderToPacket(originalBuf, label);
        
        // Act - Remove label
        var (restored, extractedLabel) = LabelHandler.RemoveLabelHeaderFromPacket(withLabel);
        
        // Assert
        extractedLabel.Should().Be(label);
        restored.Should().BeEquivalentTo(originalBuf);
    }
    
    [Fact]
    public void RemoveLabelHeaderFromPacket_TruncatedSize_ShouldThrow()
    {
        // Arrange
        var buf = new byte[] { 244 }; // hasLabelMsg but no size byte
        
        // Act
        Action act = () => LabelHandler.RemoveLabelHeaderFromPacket(buf);
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*truncated*");
    }
    
    [Fact]
    public void RemoveLabelHeaderFromPacket_EmptyLabel_ShouldThrow()
    {
        // Arrange
        var buf = new byte[] { 244, 0 }; // hasLabelMsg with zero size
        
        // Act
        Action act = () => LabelHandler.RemoveLabelHeaderFromPacket(buf);
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be empty*");
    }
    
    [Fact]
    public void RemoveLabelHeaderFromPacket_TruncatedLabel_ShouldThrow()
    {
        // Arrange
        var buf = new byte[] { 244, 5, 1, 2 }; // Says 5 bytes but only has 2
        
        // Act
        Action act = () => LabelHandler.RemoveLabelHeaderFromPacket(buf);
        
        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*truncated*");
    }
    
    [Fact]
    public void LabelOverhead_MaxSizeLabel_ShouldCalculateCorrectly()
    {
        // Arrange
        var label = new string('x', 255);
        
        // Act
        var overhead = LabelHandler.LabelOverhead(label);
        
        // Assert
        overhead.Should().Be(257); // 2 header bytes + 255 label bytes
    }
    
    [Fact]
    public void RemoveLabelHeaderFromPacket_EmptyBuffer_ShouldReturnEmpty()
    {
        // Arrange
        var buf = Array.Empty<byte>();
        
        // Act
        var (newBuf, label) = LabelHandler.RemoveLabelHeaderFromPacket(buf);
        
        // Assert
        newBuf.Should().BeSameAs(buf);
        label.Should().BeEmpty();
    }
    
    [Fact]
    public void LabelRoundTrip_UnicodeLabel_ShouldPreserveEncoding()
    {
        // Arrange
        var originalBuf = new byte[] { 1, 2, 3 };
        var label = "测试-тест-🎉";
        
        // Act
        var withLabel = LabelHandler.AddLabelHeaderToPacket(originalBuf, label);
        var (restored, extractedLabel) = LabelHandler.RemoveLabelHeaderFromPacket(withLabel);
        
        // Assert
        extractedLabel.Should().Be(label);
        restored.Should().BeEquivalentTo(originalBuf);
    }
    
    [Fact]
    public void AddLabelHeaderToPacket_NullBuffer_ShouldCreateLabelOnly()
    {
        // Arrange
        var label = "test";
        
        // Act
        var result = LabelHandler.AddLabelHeaderToPacket(Array.Empty<byte>(), label);
        
        // Assert
        result.Length.Should().Be(2 + label.Length);
        result[0].Should().Be((byte)244); // hasLabelMsg
        result[1].Should().Be((byte)label.Length);
    }
}
