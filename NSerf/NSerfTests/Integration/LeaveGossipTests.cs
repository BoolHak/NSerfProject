// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace NSerfTests.Integration;

/// <summary>
/// Tests that verify leave broadcasts are actually sent via gossip.
/// </summary>
public class LeaveGossipTests : IDisposable
{
    private readonly List<NSerf.Serf.Serf> _serfs = new();
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();

    [Fact]
    public async Task TwoNodes_LeaveAsync_BroadcastsDeadMessage()
    {
        // ARRANGE: Create two nodes
        var config1 = CreateMemberlistConfig("node1", 19101);
        var config2 = CreateMemberlistConfig("node2", 19102);

        var ml1 = NSerf.Memberlist.Memberlist.Create(config1);
        var ml2 = NSerf.Memberlist.Memberlist.Create(config2);
        _memberlists.Add(ml1);
        _memberlists.Add(ml2);

        // Join them
        var joinResult = await ml2.JoinAsync(new[] { $"127.0.0.1:{config1.BindPort}" });
        Assert.True(joinResult.NumJoined > 0, "Node2 should join node1");
        Assert.Null(joinResult.Error);

        await Task.Delay(500);

        // Verify both nodes see each other
        Assert.Equal(2, ml1.NumMembers());
        Assert.Equal(2, ml2.NumMembers());

        // ACT: Node2 leaves gracefully
        var leaveError = await ml2.LeaveAsync(TimeSpan.FromSeconds(2));
        Assert.Null(leaveError);

        // Wait for gossip to propagate
        await Task.Delay(1000);

        // ASSERT: Node1 should see node2 as Left (not Dead)
        var node2State = ml1._nodeMap.GetValueOrDefault("node2");
        Assert.NotNull(node2State);
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, node2State.State);
    }

    [Fact]
    public async Task ThreeNodes_MiddleNodeLeaves_BothReceiveDeadMessage()
    {
        // ARRANGE: Create three nodes
        var config1 = CreateMemberlistConfig("node1", 19103);
        var config2 = CreateMemberlistConfig("node2", 19104);
        var config3 = CreateMemberlistConfig("node3", 19105);

        var ml1 = NSerf.Memberlist.Memberlist.Create(config1);
        var ml2 = NSerf.Memberlist.Memberlist.Create(config2);
        var ml3 = NSerf.Memberlist.Memberlist.Create(config3);
        _memberlists.Add(ml1);
        _memberlists.Add(ml2);
        _memberlists.Add(ml3);

        // Join them into a cluster
        await ml2.JoinAsync(new[] { $"127.0.0.1:{config1.BindPort}" });
        await ml3.JoinAsync(new[] { $"127.0.0.1:{config1.BindPort}" });
        await Task.Delay(500);

        Assert.Equal(3, ml1.NumMembers());
        Assert.Equal(3, ml2.NumMembers());
        Assert.Equal(3, ml3.NumMembers());

        // ACT: Node2 leaves
        await ml2.LeaveAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(1000);

        // ASSERT: Both node1 and node3 see node2 as Left
        var node1View = ml1._nodeMap.GetValueOrDefault("node2");
        var node3View = ml3._nodeMap.GetValueOrDefault("node2");
        
        Assert.NotNull(node1View);
        Assert.NotNull(node3View);
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, node1View.State);
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, node3View.State);
    }

    [Fact]
    public async Task LeaveAsync_SendsToMultipleNodes()
    {
        // ARRANGE: Create 5 nodes for better gossip coverage
        var memberlists = new List<NSerf.Memberlist.Memberlist>();
        
        for (int i = 0; i < 5; i++)
        {
            var config = CreateMemberlistConfig($"node{i}", 19106 + i);
            var ml = NSerf.Memberlist.Memberlist.Create(config);
            memberlists.Add(ml);
            _memberlists.Add(ml);
        }

        // Join all to node0
        for (int i = 1; i < 5; i++)
        {
            await memberlists[i].JoinAsync(new[] { "127.0.0.1:19106" });
        }
        await Task.Delay(1000);

        // Verify all see 5 members
        foreach (var ml in memberlists)
        {
            Assert.Equal(5, ml.NumMembers());
        }

        // ACT: Node4 leaves
        await memberlists[4].LeaveAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(1000);

        // ASSERT: All other nodes see node4 as Left
        for (int i = 0; i < 4; i++)
        {
            var nodeView = memberlists[i]._nodeMap.GetValueOrDefault("node4");
            Assert.NotNull(nodeView);
            Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, nodeView.State);
        }
    }

    [Fact]
    public async Task SingleNode_LeaveAsync_ReturnsImmediately()
    {
        // ARRANGE: Single node cluster
        var config = CreateMemberlistConfig("solo-node", 19111);
        var ml = NSerf.Memberlist.Memberlist.Create(config);
        _memberlists.Add(ml);

        // Only 1 member (itself)
        Assert.Equal(1, ml.NumMembers());

        // ACT: Leave with no other nodes
        var startTime = DateTimeOffset.UtcNow;
        var error = await ml.LeaveAsync(TimeSpan.FromSeconds(5));
        var duration = DateTimeOffset.UtcNow - startTime;

        // ASSERT: Should return quickly since no alive nodes to gossip to
        // Go's implementation returns immediately if anyAlive() is false
        Assert.Null(error);
        Assert.True(duration.TotalSeconds < 2, $"Should return quickly, but took {duration.TotalSeconds}s");
    }

    [Fact]
    public async Task LeaveAsync_WithAllNodesAlreadyLeft_ReturnsQuickly()
    {
        // ARRANGE: Three nodes
        var ml1 = NSerf.Memberlist.Memberlist.Create(CreateMemberlistConfig("node1", 19112));
        var ml2 = NSerf.Memberlist.Memberlist.Create(CreateMemberlistConfig("node2", 19113));
        var ml3 = NSerf.Memberlist.Memberlist.Create(CreateMemberlistConfig("node3", 19114));
        _memberlists.Add(ml1);
        _memberlists.Add(ml2);
        _memberlists.Add(ml3);

        // Join them
        await ml2.JoinAsync(new[] { "127.0.0.1:19112" });
        await ml3.JoinAsync(new[] { "127.0.0.1:19112" });
        await Task.Delay(500);

        Assert.Equal(3, ml1.NumMembers());

        // ACT: Node2 and Node3 leave first
        await ml2.LeaveAsync(TimeSpan.FromSeconds(2));
        await ml3.LeaveAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(1000);

        // Node1 should see both as Left
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, 
            ml1._nodeMap["node2"].State);
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, 
            ml1._nodeMap["node3"].State);

        // Now node1 leaves (should have no alive peers)
        var startTime = DateTimeOffset.UtcNow;
        var error = await ml1.LeaveAsync(TimeSpan.FromSeconds(5));
        var duration = DateTimeOffset.UtcNow - startTime;

        // ASSERT: Should return quickly since no alive nodes
        Assert.Null(error);
        Assert.True(duration.TotalSeconds < 2, $"Should return quickly, but took {duration.TotalSeconds}s");
    }

    private NSerf.Memberlist.Configuration.MemberlistConfig CreateMemberlistConfig(string name, int port)
    {
        var config = new NSerf.Memberlist.Configuration.MemberlistConfig
        {
            Name = name,
            BindAddr = "127.0.0.1",
            BindPort = port,
            AdvertiseAddr = "127.0.0.1",
            AdvertisePort = port,
            Logger = NullLogger.Instance,
            GossipInterval = TimeSpan.FromMilliseconds(100), // Fast gossip for tests
            ProbeInterval = TimeSpan.FromSeconds(1)
        };

        // Create transport
        var transportConfig = new NSerf.Memberlist.Transport.NetTransportConfig
        {
            BindAddrs = new List<string> { config.BindAddr },
            BindPort = config.BindPort,
            Logger = NullLogger.Instance
        };
        config.Transport = NSerf.Memberlist.Transport.NetTransport.Create(transportConfig);

        return config;
    }

    public void Dispose()
    {
        foreach (var serf in _serfs)
        {
            try
            {
                serf.ShutdownAsync().GetAwaiter().GetResult();
                serf.Dispose();
            }
            catch
            {
                // Ignore shutdown errors during cleanup
            }
        }

        foreach (var ml in _memberlists)
        {
            try
            {
                ml.ShutdownAsync().GetAwaiter().GetResult();
                ml.Dispose();
            }
            catch
            {
                // Ignore shutdown errors during cleanup
            }
        }
    }
}
