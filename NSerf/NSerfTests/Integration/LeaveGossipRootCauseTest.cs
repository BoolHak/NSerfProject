// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace NSerfTests.Integration;

/// <summary>
/// Isolates the root cause: GossipAsync() returns without sending when no suitable nodes found.
/// This is the EXACT bug causing Docker leave messages to not propagate.
/// </summary>
public class LeaveGossipRootCauseTest : IDisposable
{
    private readonly List<NSerf.Memberlist.Memberlist> _memberlists = new();
    private readonly ITestOutputHelper _output;

    public LeaveGossipRootCauseTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ProveGossipAsync_DoesNotSendToLeftNodes()
    {
        // ARRANGE: Two nodes
        var config1 = CreateConfig("node1", 19201);
        var config2 = CreateConfig("node2", 19202);
        
        var ml1 = NSerf.Memberlist.Memberlist.Create(config1);
        var ml2 = NSerf.Memberlist.Memberlist.Create(config2);
        _memberlists.Add(ml1);
        _memberlists.Add(ml2);

        await ml2.JoinAsync(new[] { "127.0.0.1:19201" });
        await Task.Delay(500);

        // Both see each other
        Assert.Equal(2, ml1.NumMembers());
        Assert.Equal(2, ml2.NumMembers());

        // ACT: Mark node2 as Left on node1 (simulate node2 already left)
        lock (ml1.NodeLock)
        {
            if (ml1.NodeMap.TryGetValue("node2", out var state))
            {
                state.State = NSerf.Memberlist.State.NodeStateType.Left;
                state.StateChange = DateTimeOffset.UtcNow;
                _output.WriteLine($"Manually marked node2 as Left on node1");
            }
        }

        // Queue a broadcast on node1
        var testMsg = new NSerf.Memberlist.Messages.Alive
        {
            Node = "node1",
            Addr = System.Net.IPAddress.Parse("127.0.0.1").GetAddressBytes(),
            Port = 19201,
            Incarnation = 99
        };
        ml1.EncodeAndBroadcast("node1", NSerf.Memberlist.Messages.MessageType.Alive, testMsg);

        var queuedBefore = ml1.Broadcasts.NumQueued();
        _output.WriteLine($"Broadcasts queued: {queuedBefore}");
        Assert.True(queuedBefore > 0, "Should have queued broadcast");

        // Try to gossip
        await ml1.GossipAsync();

        var queuedAfter = ml1.Broadcasts.NumQueued();
        _output.WriteLine($"Broadcasts queued after gossip: {queuedAfter}");

        // ASSERT: Broadcast NOT sent because node2 is Left and excluded
        Assert.Equal(queuedBefore, queuedAfter);
        _output.WriteLine("✅ PROVED: GossipAsync() does not send to Left nodes!");
    }

    private NSerf.Memberlist.Configuration.MemberlistConfig CreateConfig(string name, int port)
    {
        var config = new NSerf.Memberlist.Configuration.MemberlistConfig
        {
            Name = name,
            BindAddr = "127.0.0.1",
            BindPort = port,
            AdvertiseAddr = "127.0.0.1",
            AdvertisePort = port,
            Logger = NullLogger.Instance,
            GossipInterval = TimeSpan.FromMilliseconds(200),
            ProbeInterval = TimeSpan.FromSeconds(1)
        };

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
        foreach (var ml in _memberlists)
        {
            try
            {
                ml.ShutdownAsync().GetAwaiter().GetResult();
                ml.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
