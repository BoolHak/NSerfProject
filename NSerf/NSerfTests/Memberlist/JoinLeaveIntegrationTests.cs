// Integration tests for Join/Leave operations
// Ported from: github.com/hashicorp/memberlist/memberlist_test.go
// Copyright (c) Boolhak, Inc.
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
/// Integration tests for cluster join and leave operations.
/// These tests use real network transport (NetTransport) instead of mocks.
/// </summary>
public class JoinLeaveIntegrationTests : IAsyncLifetime
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = [];

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
        config.Logger = null; // Logs written to console via _logger in Memberlist

        // Speed up convergence for tests
        config.ProbeInterval = TimeSpan.FromMilliseconds(100);
        config.ProbeTimeout = TimeSpan.FromMilliseconds(500);
        config.GossipInterval = TimeSpan.FromMilliseconds(100);
        config.PushPullInterval = TimeSpan.FromMilliseconds(500);
        config.TCPTimeout = TimeSpan.FromSeconds(2);
        config.DeadNodeReclaimTime = TimeSpan.FromSeconds(30); // Allow broadcasts for local node

        return config;
    }

    private NSerf.Memberlist.Memberlist CreateMemberlistAsync(MemberlistConfig config)
    {
        // Create real network transport
        var transportConfig = new NetTransportConfig
        {
            BindAddrs = [config.BindAddr],
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
    public async Task TestMemberlist_Join()
    {
        // Create first node
        var config1 = CreateTestConfig("node1");
        var m1 = CreateMemberlistAsync(config1);

        var bindPort = m1._config.BindPort;

        // Give the TCP listener time to start
        await Task.Delay(100);

        // Create second node on same address but different port
        var config2 = CreateTestConfig("node2");
        config2.BindPort = 0; // Different port
        var m2 = CreateMemberlistAsync(config2);

        // Give the second TCP listener time to start
        await Task.Delay(100);

        // m2 joins m1 using TCP push/pull (now with length-prefixed framing)
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";

        // Add timeout to prevent test from hanging
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync([joinAddr], cts.Token);

        error.Should().BeNull("join should succeed without errors");
        numJoined.Should().Be(1, "should successfully join 1 node");

        // Wait for convergence
        await Task.Delay(500);

        // Check both nodes see 2 members
        m1.NumMembers().Should().Be(2, "m1 should see 2 members");
        m2.NumMembers().Should().Be(2, "m2 should see 2 members");

        m1.EstNumNodes().Should().Be(2, "m1 should estimate 2 nodes");
        m2.EstNumNodes().Should().Be(2, "m2 should estimate 2 nodes");

        // Verify members list contains both nodes
        var m1Members = m1.Members();
        var m2Members = m2.Members();

        m1Members.Should().Contain(n => n.Name == "node1");
        m1Members.Should().Contain(n => n.Name == "node2");

        m2Members.Should().Contain(n => n.Name == "node1");
        m2Members.Should().Contain(n => n.Name == "node2");
    }

    [Fact]
    public async Task TestMemberlist_Leave()
    {
        // Create two nodes and join them
        var config1 = CreateTestConfig("node1");
        var m1 = CreateMemberlistAsync(config1);

        var bindPort = m1._config.BindPort;
        await Task.Delay(100);

        var config2 = CreateTestConfig("node2");
        config2.BindPort = 0;
        var m2 = CreateMemberlistAsync(config2);

        await Task.Delay(100);

        // m2 joins m1
        var joinAddr = $"{m1._config.BindAddr}:{bindPort}";
        using var joinCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (numJoined, error) = await m2.JoinAsync([joinAddr], joinCts.Token);

        error.Should().BeNull();
        numJoined.Should().Be(1);

        await Task.Delay(1500);

        // Both should see 2 members
        m1.NumMembers().Should().Be(2);
        m2.NumMembers().Should().Be(2);

        // Now m1 leaves gracefully (not m2, because we want to check from m2's perspective)
        var leaveResult = await m1.LeaveAsync(TimeSpan.FromSeconds(1));
        leaveResult.Should().BeNull("leave should not return an error");

        // Wait for leave to propagate via gossip (broadcasts are consumed quickly by gossip scheduler)
        await Task.Delay(2000);

        // m1 marks itself as Left, so Members() excludes it, but m1 still sees m2
        var m1Members = m1.Members();
        m1Members.Should().HaveCount(1, "m1 should see 1 member (m2) after marking itself as Left");
        m1Members.Should().Contain(n => n.Name == "node2", "m1 should still see m2 as alive");

        var m2Members = m2.Members();
        m2Members.Should().HaveCount(1, "m2 should only see itself after m1 leaves");
        m2Members.Should().Contain(n => n.Name == "node2");

        // Check that m1 is marked as left in m2's node map
        lock (m2._nodeLock)
        {
            if (m2._nodeMap.TryGetValue("node1", out var node1State))
            {
                node1State.State.Should().Be(NodeStateType.Left, "m1 should be marked as Left");
            }
        }
    }

    [Fact]
    public async Task TestMemberlist_JoinDeadNode()
    {
        // Try to join a node that doesn't exist / isn't listening
        var config = CreateTestConfig("node1");
        var m1 = CreateMemberlistAsync(config);

        await Task.Delay(100);

        // Try to join a non-existent node
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (numJoined, error) = await m1.JoinAsync(new[] { "127.0.0.1:9999" }, cts.Token);

        // Join should fail (either error or numJoined = 0)
        if (error == null)
        {
            numJoined.Should().Be(0, "should not successfully join a dead node");
        }
        else
        {
            error.Should().NotBeNull("should return error when joining dead node");
        }

        // Node should still only see itself
        m1.NumMembers().Should().Be(1);
    }
}
