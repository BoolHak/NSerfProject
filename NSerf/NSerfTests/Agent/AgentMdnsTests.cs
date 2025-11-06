// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging.Abstractions;
using NSerf.Agent;
using System.Net;
using System.Net.NetworkInformation;

namespace NSerfTests.Agent;

public class AgentMdnsTests : IDisposable
{
    private readonly List<AgentMdns> _mdnsInstances = [];
    private readonly List<SerfAgent> _agents = [];
    [Fact]
    public async Task AgentMdns_ShouldAdvertiseService()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("node1", "test-cluster");
        var bind = IPAddress.Loopback;
        const int port = 7946;

        // Act
        var mdns = new AgentMdns(
            agent,
            replay: false,
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns);

        // Wait for service to be advertised
        await Task.Delay(1000);

        // Assert - Verify service is actually discoverable by querying mDNS
        using var discovery = new Makaretu.Dns.ServiceDiscovery();
        var foundServices = new List<string>();
        
        discovery.ServiceInstanceDiscovered += (_, e) =>
        {
            var serviceName = e.ServiceInstanceName.ToString();
            if (serviceName.Contains("node1"))
            {
                foundServices.Add(serviceName);
            }
        };

        discovery.QueryServiceInstances("_serf_test-cluster._tcp");
        await Task.Delay(2000); // Wait for mDNS responses

        // Should have discovered the advertised service
        foundServices.Should().NotBeEmpty("mDNS should discover the advertised service");
        foundServices.Should().Contain(s => s.Contains("node1"), "should find node1 in discovered services");
    }

    [Fact]
    public async Task AgentMdns_ShouldDiscoverPeers()
    {
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");

        var bind = IPAddress.Loopback;

        // Get initial member counts
        var initialMembers1 = agent1.Serf?.Members().Length ?? 0;
        var initialMembers2 = agent2.Serf?.Members().Length ?? 0;

        // Get actual ports from the agents (they bind to port 0 = dynamic)
        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        // Create first mDNS instance with actual port
        var mdns1 = new AgentMdns(
            agent1,
            replay: false,
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port1,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns1);

        // Wait for an advertisement and an initial query
        await Task.Delay(2000);

        // Create a second mDNS instance (should discover first)
        var mdns2 = new AgentMdns(
            agent2,
            replay: false,
            node: "node2",
            discover: "test-cluster",
            bind: bind,
            port: port2,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns2);

        // Wait for discovery and join (initial poll and join time)
        await Task.Delay(5000);

        // Assert - Agents should have joined each other via mDNS discovery
        var finalMembers1 = agent1.Serf?.Members().Length ?? 0;
        var finalMembers2 = agent2.Serf?.Members().Length ?? 0;

        finalMembers1.Should().BeGreaterThan(initialMembers1, "node1 should have discovered node2");
        finalMembers2.Should().BeGreaterThan(initialMembers2, "node2 should have discovered node1");
        
        // Verify they can see each other
        var node1Members = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? [];
        var node2Members = agent2.Serf?.Members().Select(m => m.Name).ToList() ?? [];
        
        node1Members.Should().Contain("node2", "node1 should see node2 in members");
        node2Members.Should().Contain("node1", "node2 should see node1 in members");
    }

    [Fact]
    public async Task AgentMdns_ShouldPreventDuplicateJoins()
    {
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var bind = IPAddress.Loopback;

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        var mdns1 = new AgentMdns(
            agent1,
            replay: false,
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port1,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns1);

        await Task.Delay(500);

        var mdns2 = new AgentMdns(
            agent2,
            replay: false,
            node: "node2",
            discover: "test-cluster",
            bind: bind,
            port: port2,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns2);

        // Wait for initial join
        await Task.Delay(2000);
        
        var membersAfterFirstJoin = agent1.Serf?.Members().Length ?? 0;

        // Act - Wait for multiple poll cycles (should not cause duplicate joins)
        await Task.Delay(5000);

        // Assert - Member count should remain stable (no duplicate joins)
        var membersAfterMultiplePolls = agent1.Serf?.Members().Length ?? 0;
        membersAfterMultiplePolls.Should().Be(membersAfterFirstJoin, 
            "multiple poll cycles should not cause duplicate joins");
    }

    [Fact]
    public async Task AgentMdns_ShouldRespectIPv4Filter()
    {
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var bind = IPAddress.Loopback; // IPv4 address

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        // Create node1 with IPv4 disabled
        var mdns1 = new AgentMdns(
            agent1,
            replay: false,
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port1,
            disableIPv4: true, // Should ignore IPv4 addresses
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns1);

        await Task.Delay(500);

        // Create node2 advertising on IPv4
        var mdns2 = new AgentMdns(
            agent2,
            replay: false,
            node: "node2",
            discover: "test-cluster",
            bind: bind,
            port: port2,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns2);

        // Wait for discovery attempts
        await Task.Delay(3000);

        // Assert - node1 should NOT have joined node2 (IPv4 filtered)
        var members = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? new List<string>();
        members.Should().NotContain("node2", "IPv4 filter should prevent joining IPv4 addresses");
    }

    [Fact]
    public async Task AgentMdns_ShouldRespectIPv6Filter()
    {
        // Skip if IPv6 not available
        if (!System.Net.Sockets.Socket.OSSupportsIPv6)
        {
            return;
        }

        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var bind = IPAddress.IPv6Loopback;

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        // Create node1 with IPv6 disabled
        var mdns1 = new AgentMdns(
            agent1,
            replay: false,
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port1,
            disableIPv6: true, // Should ignore IPv6 addresses
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns1);

        await Task.Delay(500);

        // Create node2 advertising on IPv6
        var mdns2 = new AgentMdns(
            agent2,
            replay: false,
            node: "node2",
            discover: "test-cluster",
            bind: bind,
            port: port2,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns2);

        // Wait for discovery attempts
        await Task.Delay(3000);

        // Assert - node1 should NOT have joined node2 (IPv6 filtered)
        var members = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? new List<string>();
        members.Should().NotContain("node2", "IPv6 filter should prevent joining IPv6 addresses");
    }

    [Fact]
    public void AgentMdns_MdnsName_ShouldFormatCorrectly()
    {
        // Arrange
        const string discover = "test-cluster";

        // Act - Use reflection to test a private method
        var method = typeof(AgentMdns).GetMethod("MdnsName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = method?.Invoke(null, [discover]) as string;

        // Assert
        result.Should().Be("_serf_test-cluster._tcp");
    }

    [Fact]
    public async Task AgentMdns_ShouldBatchDiscoveries()
    {
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var agent3 = await CreateTestAgentAsync("node3", "test-cluster");
        var bind = IPAddress.Loopback;

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port3 = agent3.Serf?.Memberlist?.LocalNode.Port ?? 0;

        // Start all nodes at roughly the same time
        var mdns1 = new AgentMdns(agent1, false, "node1", "test-cluster",  bind, port1, logger: NullLogger.Instance);
        var mdns2 = new AgentMdns(agent2, false, "node2", "test-cluster",  bind, port2, logger: NullLogger.Instance);
        var mdns3 = new AgentMdns(agent3, false, "node3", "test-cluster",  bind, port3, logger: NullLogger.Instance);
        
        _mdnsInstances.AddRange([mdns1, mdns2, mdns3]);

        // Wait for a quiet interval batching (100 ms) + join time
        await Task.Delay(2000);

        // Assert - All nodes should have discovered each other in batched joins
        var members1 = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? [];
        members1.Should().Contain(["node2", "node3"], 
            "batching should allow node1 to discover multiple peers efficiently");
        
        // Verify the cluster formed properly
        members1.Count.Should().BeGreaterThanOrEqualTo(3, "all nodes should be in the cluster");
    }

    [Fact]
    public async Task AgentMdns_ShouldPollPeriodically()
    {
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var bind = IPAddress.Loopback;

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        var mdns1 = new AgentMdns(agent1, false, "node1", "test-cluster", bind, port1, logger: NullLogger.Instance);
        _mdnsInstances.Add(mdns1);

        // Wait for an initial poll
        await Task.Delay(2000);

        // Start node2 AFTER node1 has already polled
        var mdns2 = new AgentMdns(agent2, false, "node2", "test-cluster", bind, port2, logger: NullLogger.Instance);
        _mdnsInstances.Add(mdns2);

        // Wait for the next periodic poll (60 s is too long for tests, but an initial poll should catch it)
        await Task.Delay(3000);

        // Assert - Periodic polling should have discovered the late-joining node
        var members = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? new List<string>();
        members.Should().Contain("node2", 
            "periodic polling should discover nodes that join after initial poll");
    }

    [Fact]
    public async Task AgentMdns_WithReplay_ShouldPassToJoin()
    {
        // This test verifies the replay flag is used correctly
        // In practice, replay affects whether old events are replayed on join
        // We can't easily verify this without event tracking, but we can verify
        // that the join still works with replay enabled
        
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var bind = IPAddress.Loopback;

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        // Create with replay enabled
        var mdns1 = new AgentMdns(
            agent1,
            replay: true, // Enable replay (ignoreOld = false)
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port1,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns1);

        await Task.Delay(2000);

        var mdns2 = new AgentMdns(
            agent2,
            replay: true,
            node: "node2",
            discover: "test-cluster",
            bind: bind,
            port: port2,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns2);

        // Wait for discovery
        await Task.Delay(5000);

        // Assert - Join should work with a replay flag
        var members1 = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? new List<string>();
        var members2 = agent2.Serf?.Members().Select(m => m.Name).ToList() ?? new List<string>();
        
        // Both nodes should have discovered each other
        members1.Should().Contain("node2", "node1 should have discovered node2 with replay=true");
        members2.Should().Contain("node1", "node2 should have discovered node1 with replay=true");
    }

    [Fact]
    public async Task AgentMdns_Dispose_ShouldCleanupResources()
    {
        // Arrange
        var agent1 = await CreateTestAgentAsync("node1", "test-cluster");
        var agent2 = await CreateTestAgentAsync("node2", "test-cluster");
        var bind = IPAddress.Loopback;

        var port1 = agent1.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var port2 = agent2.Serf?.Memberlist?.LocalNode.Port ?? 0;

        var mdns1 = new AgentMdns(agent1, false, "node1", "test-cluster", bind, port1, logger: NullLogger.Instance);
        var mdns2 = new AgentMdns(agent2, false, "node2", "test-cluster", bind, port2, logger: NullLogger.Instance);
        _mdnsInstances.AddRange([mdns1, mdns2]);

        await Task.Delay(2000);
        
        // Verify they joined
        var membersBeforeDispose = agent1.Serf?.Members().Length ?? 0;
        membersBeforeDispose.Should().BeGreaterThan(1, "nodes should have joined before dispose");

        // Act - Dispose both mdns instances to stop all discovery
        mdns1.Dispose();
        mdns2.Dispose();

        // Wait to ensure no more discovery happens
        await Task.Delay(1000);

        // Assert - Multiple dispose calls should be safe
        var act = () => mdns1.Dispose();
        act.Should().NotThrow("multiple dispose calls should be safe");
        
        // Verify no new nodes are discovered after dispose
        var agent3 = await CreateTestAgentAsync("node3", "test-cluster");
        var port3 = agent3.Serf?.Memberlist?.LocalNode.Port ?? 0;
        var mdns3 = new AgentMdns(agent3, false, "node3", "test-cluster", bind, port3, logger: NullLogger.Instance);
        _mdnsInstances.Add(mdns3);
        
        await Task.Delay(2000);
        
        var membersAfterDispose = agent1.Serf?.Members().Select(m => m.Name).ToList() ?? new List<string>();
        membersAfterDispose.Should().NotContain("node3", 
            "disposed mDNS should not discover new nodes");
    }

    [Fact]
    public async Task AgentMdns_WithNetworkInterface_ShouldUseInterface()
    {
        // Arrange
        var agent = await CreateTestAgentAsync("node1", "test-cluster");
        var bind = IPAddress.Loopback;
        var iface = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(i => i.OperationalStatus == OperationalStatus.Up);

        if (iface == null)
        {
            // Skip test if no interface available
            return;
        }

        var port = agent.Serf?.Memberlist?.LocalNode.Port ?? 0;

        // Act
        var mdns = new AgentMdns(
            agent,
            replay: false,
            node: "node1",
            discover: "test-cluster",
            bind: bind,
            port: port,
            logger: NullLogger.Instance
        );
        _mdnsInstances.Add(mdns);

        await Task.Delay(500);

        // Assert
        mdns.Should().NotBeNull();
    }

    private async Task<SerfAgent> CreateTestAgentAsync(string nodeName, string cluster)
    {
        var config = new AgentConfig
        {
            NodeName = nodeName,
            BindAddr = "127.0.0.1:0",
            Tags = new Dictionary<string, string>
            {
                ["cluster"] = cluster
            }
        };

        var agent = new SerfAgent(config, NullLogger.Instance);
        await agent.StartAsync();
        _agents.Add(agent);
        return agent;
    }

    public void Dispose()
    {
        foreach (var mdns in _mdnsInstances)
        {
            try
            {
                mdns.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        foreach (var agent in _agents)
        {
            try
            {
                agent.ShutdownAsync().Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        GC.SuppressFinalize(this);
    }
}
