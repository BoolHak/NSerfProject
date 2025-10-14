// Ported from: github.com/hashicorp/memberlist/util_test.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Common;
using NSerf.Memberlist.Messages;

namespace NSerfTests.Memberlist.Common;

public class CompressionUtilsTests
{
    [Fact]
    public void CompressPayload_DecompressPayload_ShouldRoundTrip()
    {
        // Arrange
        var testData = "testing compression with some data that should compress well"u8.ToArray();
        
        // Act - Compress
        var compressed = CompressionUtils.CompressPayload(testData);
        
        // Assert - Compressed data should be different than original
        compressed.Should().NotBeEquivalentTo(testData);
        
        // Act - Decompress
        var decompressed = CompressionUtils.DecompressPayload(compressed);
        
        // Assert - Should match original
        decompressed.Should().BeEquivalentTo(testData);
    }
    
    [Fact]
    public void CompressPayload_EmptyInput_ShouldHandleGracefully()
    {
        // Arrange
        var emptyData = Array.Empty<byte>();
        
        // Act
        var compressed = CompressionUtils.CompressPayload(emptyData);
        var decompressed = CompressionUtils.DecompressPayload(compressed);
        
        // Assert
        decompressed.Should().BeEmpty();
    }
    
    [Fact]
    public void CompressPayload_LargeData_ShouldCompress()
    {
        // Arrange - Create large repetitive data
        var largeData = new byte[10000];
        for (int i = 0; i < largeData.Length; i++)
        {
            largeData[i] = (byte)(i % 256);
        }
        
        // Act
        var compressed = CompressionUtils.CompressPayload(largeData);
        var decompressed = CompressionUtils.DecompressPayload(compressed);
        
        // Assert
        decompressed.Should().BeEquivalentTo(largeData);
        // Compressed size should be smaller than original
        compressed.Length.Should().BeLessThan(largeData.Length);
    }
    
    [Fact]
    public void DecompressPayload_InvalidData_ShouldThrow()
    {
        // Arrange - Create invalid GZip data
        var invalidMsg = new byte[] { 0x91, 0x99, 0x00 }; // Invalid GZip format
        
        // Act
        Action act = () => CompressionUtils.DecompressPayload(invalidMsg);
        
        // Assert - Should throw InvalidDataException
        act.Should().Throw<System.IO.InvalidDataException>();
    }
}
