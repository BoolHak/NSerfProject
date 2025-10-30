// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Agent;
using Xunit;

namespace NSerfTests.Agent;

public class AgentMdnsTests
{
    [Fact]
    public void AgentMdns_Constructor_SetsServiceName()
    {
        // Arrange
        const string expectedService = "serf";
        
        // Act
        var mdns = new AgentMdns(expectedService);
        
        // Assert
        Assert.Equal(expectedService, mdns.ServiceName);
        
        mdns.Dispose();
    }

    [Fact]
    public void AgentMdns_ServiceNameUsedInQuery()
    {
        // Arrange
        const string serviceName = "test-cluster";
        var mdns = new AgentMdns(serviceName);
        
        // Act - Get the query through reflection to verify service name is used
        var buildMdnsQueryMethod = typeof(AgentMdns).GetMethod("BuildMdnsQuery", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var query = (byte[])buildMdnsQueryMethod!.Invoke(mdns, null)!;
        
        // Assert - Verify the service name is embedded in the DNS-encoded query
        // DNS format: length byte + label for each part
        var queryStr = System.Text.Encoding.UTF8.GetString(query);
        
        // Check for DNS-encoded format: \x0ctest-cluster\x04_tcp\x05local\x00
        Assert.Contains("\x0ctest-cluster", queryStr);
        Assert.Contains("\x04_tcp", queryStr);
        Assert.Contains("\x05local", queryStr);
        
        mdns.Dispose();
    }

    [Fact]
    public void AgentMdns_Start_InitializesCorrectly()
    {
        // Arrange
        var mdns = new AgentMdns("test-service", "local", 0); // Port 0 = dynamic assignment
        
        // Act
        var started = mdns.Start();
        
        // Assert - Verify the service started successfully
        Assert.True(started);
        Assert.True(mdns.IsStarted);
        
        // Clean up
        mdns.Dispose();
    }

    [Fact]
    public void AgentMdns_Start_WhenCalledTwice_ReturnsFalse()
    {
        // Arrange
        var mdns = new AgentMdns("test-service", "local", 0); // Port 0 = dynamic assignment
        var firstStart = mdns.Start();
        Assert.True(firstStart);
        
        // Act & Assert - Second start should return false (already started)
        var secondStart = mdns.Start();
        Assert.False(secondStart);
        
        mdns.Dispose();
    }

    [Fact]
    public void AgentMdns_Start_AfterDispose_ThrowsObjectDisposed()
    {
        // Arrange
        var mdns = new AgentMdns("test-service");
        mdns.Dispose();
        
        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => mdns.Start());
    }

    [Fact]
    public async Task AgentMdns_DiscoverPeers_ReturnsEmptyIfNotStarted()
    {
        var mdns = new AgentMdns("serf");

        var peers = await mdns.DiscoverPeersAsync(TimeSpan.FromMilliseconds(100));

        Assert.Empty(peers);
        mdns.Dispose();
    }

    [Fact]
    public async Task AgentMdns_DiscoverPeers_RespectsTimeout()
    {
        var mdns = new AgentMdns("serf");

        try
        {
            mdns.Start();
        }
        catch
        {
            // Port may be in use
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var peers = await mdns.DiscoverPeersAsync(TimeSpan.FromMilliseconds(500));
        sw.Stop();

        // Should complete within timeout + 200ms buffer
        Assert.True(sw.Elapsed.TotalMilliseconds < 700);

        mdns.Dispose();
    }

    [Fact]
    public void AgentMdns_Dispose_MultipleCallsSafe()
    {
        var mdns = new AgentMdns("serf");
        mdns.Dispose();
        mdns.Dispose(); // Should not throw
    }

    [Fact]
    public void AgentMdns_CustomDomain_Accepted()
    {
        // Arrange
        const string expectedService = "serf";
        const string expectedDomain = "custom.domain";
        
        // Act
        var mdns = new AgentMdns(expectedService, expectedDomain);
        
        // Assert
        Assert.Equal(expectedService, mdns.ServiceName);
        Assert.Equal(expectedDomain, mdns.Domain);
        
        mdns.Dispose();
    }

    [Fact]
    public async Task AgentMdns_AfterDispose_ReturnsEmpty()
    {
        var mdns = new AgentMdns("serf");
        mdns.Dispose();

        var peers = await mdns.DiscoverPeersAsync(TimeSpan.FromMilliseconds(100));

        Assert.Empty(peers);
    }
}
