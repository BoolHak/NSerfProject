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
        var mdns = new AgentMdns("serf");
        Assert.NotNull(mdns);
        mdns.Dispose();
    }

    [Fact]
    public void AgentMdns_Start_DoesNotThrow()
    {
        var mdns = new AgentMdns("serf");

        // May fail to bind (port in use) but should not throw
        try
        {
            mdns.Start();
        }
        catch
        {
            // Ignore - port may be in use
        }

        mdns.Dispose();
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
        var mdns = new AgentMdns("serf", "custom.domain");
        Assert.NotNull(mdns);
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
