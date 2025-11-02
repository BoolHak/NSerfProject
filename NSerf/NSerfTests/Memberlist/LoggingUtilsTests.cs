// Ported from: github.com/hashicorp/memberlist/logging.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using NSerf.Memberlist.Common;

namespace NSerfTests.Memberlist;

public class LoggingUtilsTests
{
    [Fact]
    public void LogAddress_Null_ShouldReturnUnknown()
    {
        // Act
        var result = LoggingUtils.LogAddress(null);
        
        // Assert
        result.Should().Be("from=<unknown address>");
    }
    
    [Fact]
    public void LogAddress_ValidEndpoint_ShouldFormat()
    {
        // Arrange
        var endpoint = new IPEndPoint(IPAddress.Parse("192.168.1.1"), 8080);
        
        // Act
        var result = LoggingUtils.LogAddress(endpoint);
        
        // Assert
        result.Should().StartWith("from=");
        result.Should().Contain("192.168.1.1");
        result.Should().Contain("8080");
    }
    
    [Fact]
    public void LogStringAddress_Null_ShouldReturnUnknown()
    {
        // Act
        var result = LoggingUtils.LogStringAddress(null);
        
        // Assert
        result.Should().Be("from=<unknown address>");
    }
    
    [Fact]
    public void LogStringAddress_Empty_ShouldReturnUnknown()
    {
        // Act
        var result = LoggingUtils.LogStringAddress("");
        
        // Assert
        result.Should().Be("from=<unknown address>");
    }
    
    [Fact]
    public void LogStringAddress_ValidAddress_ShouldFormat()
    {
        // Arrange
        var address = "192.168.1.1:8080";
        
        // Act
        var result = LoggingUtils.LogStringAddress(address);
        
        // Assert
        result.Should().Be("from=192.168.1.1:8080");
    }
    
    [Fact]
    public void LogStream_Null_ShouldReturnUnknown()
    {
        // Act
        var result = LoggingUtils.LogStream(null);
        
        // Assert
        result.Should().Be("from=<unknown address>");
    }
}
