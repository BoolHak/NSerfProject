// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class CircularBufferTests
{
    [Fact]
    public void CircularBuffer_WriteLessThanSize_ReturnsExactContent()
    {
        var buffer = new CircularBuffer(1024);
        var data = System.Text.Encoding.UTF8.GetBytes("Hello World");
        
        buffer.Write(data);
        
        Assert.Equal(11, buffer.TotalWritten);
        Assert.False(buffer.WasTruncated);
        Assert.Equal("Hello World", buffer.GetString());
    }

    [Fact]
    public void CircularBuffer_WriteMoreThanSize_Truncates()
    {
        var buffer = new CircularBuffer(10);
        var data = System.Text.Encoding.UTF8.GetBytes("Hello World This Is Long");
        
        buffer.Write(data);
        
        Assert.Equal(24, buffer.TotalWritten);  // "Hello World This Is Long" is 24 bytes
        Assert.True(buffer.WasTruncated);
        Assert.Equal(10, buffer.GetBytes().Length);
    }

    [Fact]
    public void CircularBuffer_Wraps_MaintainsNewestData()
    {
        var buffer = new CircularBuffer(5);
        buffer.Write(System.Text.Encoding.UTF8.GetBytes("12345"));
        buffer.Write(System.Text.Encoding.UTF8.GetBytes("67890"));
        
        Assert.Equal(10, buffer.TotalWritten);
        Assert.True(buffer.WasTruncated);
        Assert.Equal("67890", buffer.GetString());
    }

    [Fact]
    public void CircularBuffer_Reset_ClearsState()
    {
        var buffer = new CircularBuffer(10);
        buffer.Write(System.Text.Encoding.UTF8.GetBytes("test"));
        
        buffer.Reset();
        
        Assert.Equal(0, buffer.TotalWritten);
        Assert.False(buffer.WasTruncated);
        Assert.Equal("", buffer.GetString());
    }
}
