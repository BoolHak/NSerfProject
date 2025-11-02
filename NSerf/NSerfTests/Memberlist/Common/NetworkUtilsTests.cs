// Ported from: github.com/hashicorp/memberlist/util_test.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Common;

namespace NSerfTests.Memberlist.Common;

public class NetworkUtilsTests
{
    [Theory]
    [InlineData("1.2.3.4", false, "1.2.3.4:8301")]
    [InlineData("1.2.3.4:1234", true, "1.2.3.4:1234")]
    [InlineData("2600:1f14:e22:1501:f9a:2e0c:a167:67e8", false, "[2600:1f14:e22:1501:f9a:2e0c:a167:67e8]:8301")]
    [InlineData("[2600:1f14:e22:1501:f9a:2e0c:a167:67e8]", false, "[2600:1f14:e22:1501:f9a:2e0c:a167:67e8]:8301")]
    [InlineData("[2600:1f14:e22:1501:f9a:2e0c:a167:67e8]:1234", true, "[2600:1f14:e22:1501:f9a:2e0c:a167:67e8]:1234")]
    [InlineData("localhost", false, "localhost:8301")]
    [InlineData("localhost:1234", true, "localhost:1234")]
    [InlineData("hashicorp.com", false, "hashicorp.com:8301")]
    [InlineData("hashicorp.com:1234", true, "hashicorp.com:1234")]
    public void PortFunctions_ShouldWorkCorrectly(string addr, bool expectedHasPort, string expectedEnsurePort)
    {
        // Act - HasPort
        var hasPort = NetworkUtils.HasPort(addr);
        
        // Assert - HasPort
        hasPort.Should().Be(expectedHasPort, $"HasPort check for {addr}");
        
        // Act - EnsurePort
        var ensurePort = NetworkUtils.EnsurePort(addr, 8301);
        
        // Assert - EnsurePort
        ensurePort.Should().Be(expectedEnsurePort, $"EnsurePort check for {addr}");
    }
    
    [Theory]
    [InlineData("192.168.1.1", 8080, "192.168.1.1:8080")]
    [InlineData("::1", 8080, "[::1]:8080")]
    [InlineData("2001:db8::1", 443, "[2001:db8::1]:443")]
    [InlineData("example.com", 443, "example.com:443")]
    public void JoinHostPort_ShouldFormatCorrectly(string host, int port, string expected)
    {
        // Act
        var result = NetworkUtils.JoinHostPort(host, (ushort)port);
        
        // Assert
        result.Should().Be(expected);
    }
}
