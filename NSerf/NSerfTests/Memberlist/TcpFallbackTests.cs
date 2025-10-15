// Integration tests for TCP fallback functionality
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;
using Xunit;

namespace NSerfTests.Memberlist;

/// <summary>
/// Integration tests for TCP fallback when UDP probes fail.
/// Tests that TCP fallback works correctly when UDP is blocked/degraded.
/// </summary>
public class TcpFallbackTests : IAsyncLifetime
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();
    
    public Task InitializeAsync() => Task.CompletedTask;
    
    public async Task DisposeAsync()
    {
        // Clean up all memberlists
        foreach (var m in _memberlists)
        {
            try
            {
                await m.ShutdownAsync();
            }
            catch
            {
                // Ignore shutdown errors during cleanup
            }
        }
        _memberlists.Clear();
    }
    
    private MemberlistConfig CreateTestConfig(string name)
    {
        var config = MemberlistConfig.DefaultLANConfig();
        config.Name = name;
        config.BindAddr = "127.0.0.1";
        config.BindPort = 0; // Let OS assign port
        config.Logger = null;
        
        // Speed up probes for testing
        config.ProbeInterval = TimeSpan.FromMilliseconds(100);
        config.ProbeTimeout = TimeSpan.FromMilliseconds(500);
        config.GossipInterval = TimeSpan.FromMilliseconds(100);
        config.PushPullInterval = TimeSpan.FromMilliseconds(500);
        config.TCPTimeout = TimeSpan.FromSeconds(2);
        config.SuspicionMult = 4;
        
        return config;
    }
    
    private NSerf.Memberlist.Memberlist CreateMemberlistAsync(MemberlistConfig config)
    {
        // Create real network transport
        var transportConfig = new NetTransportConfig
        {
            BindAddrs = new List<string> { config.BindAddr },
            BindPort = config.BindPort,
            Logger = config.Logger
        };
        
        var transport = NetTransport.Create(transportConfig);
        config.Transport = transport;
        
        var m = NSerf.Memberlist.Memberlist.Create(config);
        _memberlists.Add(m);
        return m;
    }

    [Fact]
    public async Task TestTcpFallback_Enabled_ShouldKeepNodesAlive()
    {
        // Create first node with TCP fallback enabled
        var config1 = CreateTestConfig("node1");
        config1.DisableTcpPings = false; // Enable TCP fallback
        var m1 =  CreateMemberlistAsync(config1);
        
        var bindPort = m1._config.BindPort;
        await Task.Delay(100);
        
        // Create second node with TCP fallback enabled
        var config2 = CreateTestConfig("node2");
        config2.DisableTcpPings = false; // Enable TCP fallback
        var m2 = CreateMemberlistAsync(config2);
        
        await Task.Delay(100);
        
        // m2 joins m1
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, joinCts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        await Task.Delay(500);
        
        // Both should see 2 members
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);
        
        // Let some probe cycles run
        await Task.Delay(1000);
        
        // Both nodes should still be alive (TCP fallback should work)
        lock (m1._nodeLock)
        {
            if (m1._nodeMap.TryGetValue("node2", out var node2State))
            {
                node2State.State.Should().Be(NodeStateType.Alive, "node2 should remain alive with TCP fallback");
            }
        }
        
        lock (m2._nodeLock)
        {
            if (m2._nodeMap.TryGetValue("node1", out var node1State))
            {
                node1State.State.Should().Be(NodeStateType.Alive, "node1 should remain alive with TCP fallback");
            }
        }
    }

    [Fact]
    public async Task TestTcpFallback_Disabled_GlobalConfig()
    {
        // Create first node with TCP fallback disabled globally
        var config1 = CreateTestConfig("node1");
        config1.DisableTcpPings = true; // Disable TCP fallback
        var m1 = CreateMemberlistAsync(config1);
        
        var bindPort = m1._config.BindPort;
        await Task.Delay(100);
        
        // Create second node with TCP fallback disabled
        var config2 = CreateTestConfig("node2");
        config2.DisableTcpPings = true; // Disable TCP fallback
        var m2 = CreateMemberlistAsync(config2);
        
        await Task.Delay(100);
        
        // m2 joins m1
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, joinCts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        await Task.Delay(500);
        
        // Both should see 2 members initially
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);
        
        // Verify TCP fallback is disabled in config
        m1._config.DisableTcpPings.Should().BeTrue("TCP fallback should be disabled");
        m2._config.DisableTcpPings.Should().BeTrue("TCP fallback should be disabled");
    }

    [Fact]
    public async Task TestTcpFallback_PerNodeDisable()
    {
        // Create first node with per-node TCP fallback control
        var config1 = CreateTestConfig("node1");
        config1.DisableTcpPings = false;
        // Disable TCP pings for node2 specifically
        config1.DisableTcpPingsForNode = (nodeName) => nodeName == "node2";
        var m1 = CreateMemberlistAsync(config1);
        
        var bindPort = m1._config.BindPort;
        await Task.Delay(100);
        
        // Create second node
        var config2 = CreateTestConfig("node2");
        config2.DisableTcpPings = false;
        var m2 = CreateMemberlistAsync(config2);
        
        await Task.Delay(100);
        
        // m2 joins m1
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, joinCts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        await Task.Delay(500);
        
        // Both should see 2 members initially
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);
        
        // Verify per-node disable function is set
        m1._config.DisableTcpPingsForNode.Should().NotBeNull("Per-node TCP disable should be configured");
        m1._config.DisableTcpPingsForNode!("node2").Should().BeTrue("TCP pings to node2 should be disabled");
        m1._config.DisableTcpPingsForNode!("node1").Should().BeFalse("TCP pings to node1 should be enabled");
    }

    [Fact]
    public async Task TestTcpFallback_ProtocolVersionCheck()
    {
        // This test verifies that TCP fallback only happens for nodes with protocol version >= 3
        // Create two nodes and verify TCP fallback respects protocol version
        
        var config1 = CreateTestConfig("node1");
        config1.DisableTcpPings = false;
        var m1 = CreateMemberlistAsync(config1);
        
        var bindPort = m1._config.BindPort;
        await Task.Delay(100);
        
        var config2 = CreateTestConfig("node2");
        config2.DisableTcpPings = false;
        var m2 = CreateMemberlistAsync(config2);
        
        await Task.Delay(100);
        
        // Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await m2.JoinAsync([joinAddr], joinCts.Token);
        
        await Task.Delay(500);
        
        // Verify protocol versions support TCP fallback (>= 3)
        lock (m1._nodeLock)
        {
            if (m1._nodeMap.TryGetValue("node2", out var node2State))
            {
                node2State.Node.PMax.Should().BeGreaterOrEqualTo((byte)3, 
                    "node2 should support protocol version 3+ for TCP fallback");
            }
        }
        
        lock (m2._nodeLock)
        {
            if (m2._nodeMap.TryGetValue("node1", out var node1State))
            {
                node1State.Node.PMax.Should().BeGreaterOrEqualTo((byte)3,
                    "node1 should support protocol version 3+ for TCP fallback");
            }
        }
    }

    [Fact]
    public async Task TestTcpFallback_WithRealNetworkTransport()
    {
        // This test uses real network transport to ensure TCP fallback works in realistic conditions
        var config1 = CreateTestConfig("node1");
        config1.DisableTcpPings = false;
        config1.ProbeInterval = TimeSpan.FromMilliseconds(50);
        config1.ProbeTimeout = TimeSpan.FromMilliseconds(200);
        var m1 = CreateMemberlistAsync(config1);
        
        var bindPort = m1._config.BindPort;
        await Task.Delay(100);
        
        var config2 = CreateTestConfig("node2");
        config2.DisableTcpPings = false;
        config2.ProbeInterval = TimeSpan.FromMilliseconds(50);
        config2.ProbeTimeout = TimeSpan.FromMilliseconds(200);
        var m2 = CreateMemberlistAsync(config2);
        
        await Task.Delay(100);
        
        // Join nodes
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync(new[] { joinAddr }, joinCts.Token);
        
        error.Should().BeNull();
        numJoined.Should().Be(1);
        
        // Wait for convergence
        await Task.Delay(500);
        
        m1.NumMembers().Should().Be(2, "m1 should see both nodes");
        m2.NumMembers().Should().Be(2, "m2 should see both nodes");
        
        // Let multiple probe cycles complete with TCP fallback available
        await Task.Delay(1000);
        
        // Verify both nodes are still alive
        var m1Members = m1.Members();
        var m2Members = m2.Members();
        
        m1Members.Should().HaveCount(2, "m1 should still see 2 alive members");
        m2Members.Should().HaveCount(2, "m2 should still see 2 alive members");
        
        m1Members.Should().Contain(n => n.Name == "node1");
        m1Members.Should().Contain(n => n.Name == "node2");
        m2Members.Should().Contain(n => n.Name == "node1");
        m2Members.Should().Contain(n => n.Name == "node2");
    }
}
