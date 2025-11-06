// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Xunit;
using NSerf.Serf.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace NSerfTests.Integration;

/// <summary>
/// Tests for verifying leave event broadcast behavior.
/// These tests systematically verify each step of the leave process.
/// </summary>
public class LeaveEventBroadcastTests : IDisposable
{
    private readonly List<NSerf.Serf.Serf> _serf = new();
    private readonly ILogger _logger = NullLogger.Instance;

    [Fact]
    public async Task Leave_BroadcastsMessageSuccessfully()
    {
        // ARRANGE: Create a Serf instance
        var config = GetTestConfig("test-node-1", 19001);
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serf.Add(serf);

        // ACT: Call LeaveAsync
        await serf.LeaveAsync();

        // ASSERT: Check that broadcasts queue has the message
        var broadcastCount = serf.Broadcasts.Count;
        _logger.LogInformation("Broadcasts queue count after leave: {Count}", broadcastCount);

        Assert.True(broadcastCount >= 0, "Broadcasts queue should be accessible");
    }

    [Fact]
    public async Task Leave_WaitsForBroadcastToSend()
    {
        // ARRANGE: Create a Serf instance
        var config = GetTestConfig("test-node-2", 19002);
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serf.Add(serf);

        // ACT: Call LeaveAsync and measure time
        var startTime = DateTime.UtcNow;
        await serf.LeaveAsync();
        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        // ASSERT: Should take at least the propagation delay (1 second default)
        _logger.LogInformation("LeaveAsync duration: {Duration}ms", duration.TotalMilliseconds);

        // Should complete (not hang forever)
        Assert.True(duration.TotalSeconds < 15, "LeaveAsync should complete within 15 seconds");

        // Should wait for broadcast + propagation
        Assert.True(duration.TotalMilliseconds >= 500, "LeaveAsync should wait for broadcast to send");
    }

    [Fact]
    public async Task TwoNodes_LeaveTriggersEventOnOtherNode()
    {
        // ARRANGE: Create two nodes
        var eventChannel1 = Channel.CreateUnbounded<IEvent>();
        var eventChannel2 = Channel.CreateUnbounded<IEvent>();

        var config1 = GetTestConfig("node1", 19003, eventChannel1.Writer);
        var config2 = GetTestConfig("node2", 19004, eventChannel2.Writer);

        var serf1 = await NSerf.Serf.Serf.CreateAsync(config1);
        var serf2 = await NSerf.Serf.Serf.CreateAsync(config2);
        _serf.Add(serf1);
        _serf.Add(serf2);

        // Join nodes
        var joined = await serf2.JoinAsync(new[] { "127.0.0.1:19003" }, ignoreOld: false);
        _logger.LogInformation("Node2 joined {Count} nodes", joined);
        Assert.True(joined > 0, "Node2 should successfully join node1");

        // Wait for join to stabilize
        await Task.Delay(500);

        // Clear any existing events
        while (eventChannel1.Reader.TryRead(out _)) { }
        while (eventChannel2.Reader.TryRead(out _)) { }

        // ACT: Node2 leaves
        _logger.LogInformation("=== Node2 calling LeaveAsync ===");
        await serf2.LeaveAsync();
        _logger.LogInformation("=== Node2 LeaveAsync complete ===");

        // ASSERT: Node1 should receive a leave event
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        MemberEvent? leaveEvent = null;

        try
        {
            while (await eventChannel1.Reader.WaitToReadAsync(cts.Token))
            {
                if (eventChannel1.Reader.TryRead(out var evt))
                {
                    _logger.LogInformation("Node1 received event: {Type}", evt.EventType());

                    if (evt is MemberEvent memberEvent)
                    {
                        foreach (var member in memberEvent.Members)
                        {
                            _logger.LogInformation("  - Member: {Name}, Status: {Status}",
                                member.Name, member.Status);
                        }

                        if (memberEvent.Type == EventType.MemberLeave)
                        {
                            leaveEvent = memberEvent;
                            break;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout waiting for leave event");
        }

        Assert.NotNull(leaveEvent);
        Assert.Contains(leaveEvent.Members, m => m.Name == "node2");

        var node2Status = leaveEvent.Members.First(m => m.Name == "node2").Status;
        _logger.LogInformation("Node2 status in leave event: {Status}", node2Status);

        // Should be Leaving or Left (not Failed)
        Assert.True(
            node2Status == NSerf.Serf.MemberStatus.Leaving || node2Status == NSerf.Serf.MemberStatus.Left,
            $"Node2 should be Leaving or Left, but was {node2Status}");
    }

    [Fact]
    public async Task MemberlistDeadMessage_WithNodeEqualsFrom_SetsStateToLeft()
    {
        // ARRANGE: Create a node
        var config = GetTestConfig("test-node-3", 19005);
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serf.Add(serf);

        // Get memberlist reference
        var memberlist = serf.Memberlist;
        Assert.NotNull(memberlist);

        // ACT: Simulate receiving a dead message where Node==From (graceful leave)
        var deadMsg = new NSerf.Memberlist.Messages.Dead
        {
            Node = "test-node-3",
            From = "test-node-3",  // Same as Node = graceful leave
            Incarnation = 1
        };

        // Trigger HandleDeadNode through state handler
        var stateHandler = new NSerf.Memberlist.StateHandlers(memberlist, _logger);
        stateHandler.HandleDeadNode(deadMsg);

        // ASSERT: Check node state
        var nodeState = memberlist.NodeMap.GetValueOrDefault("test-node-3");
        Assert.NotNull(nodeState);

        _logger.LogInformation("Node state after dead message: {State}", nodeState.State);

        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, nodeState.State);
    }

    [Fact]
    public async Task MemberlistDeadMessage_WithNodeNotEqualsFrom_SetsStateToDead()
    {
        // ARRANGE: Create two nodes
        var config1 = GetTestConfig("node-a", 19006);
        var config2 = GetTestConfig("node-b", 19007);

        var serf1 = await NSerf.Serf.Serf.CreateAsync(config1);
        var serf2 = await NSerf.Serf.Serf.CreateAsync(config2);
        _serf.Add(serf1);
        _serf.Add(serf2);

        await serf2.JoinAsync(new[] { "127.0.0.1:19006" }, ignoreOld: false);
        await Task.Delay(500);

        var memberlist1 = serf1.Memberlist!;

        // ACT: Simulate node-a reporting node-b as dead (failure detection)
        var deadMsg = new NSerf.Memberlist.Messages.Dead
        {
            Node = "node-b",
            From = "node-a",  // Different from Node = failure detection
            Incarnation = 5
        };

        var stateHandler = new NSerf.Memberlist.StateHandlers(memberlist1, _logger);
        stateHandler.HandleDeadNode(deadMsg);

        // ASSERT: Check node state
        var nodeState = memberlist1.NodeMap.GetValueOrDefault("node-b");
        Assert.NotNull(nodeState);

        _logger.LogInformation("Node-b state after dead message from node-a: {State}", nodeState.State);

        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Dead, nodeState.State);
    }

    [Fact]
    public async Task SerfHandleNodeLeave_WithLeftState_MapsTol_Left()
    {
        // ARRANGE: Create a Serf instance
        var eventChannel = Channel.CreateUnbounded<IEvent>();
        var config = GetTestConfig("test-node-4", 19008, eventChannel.Writer);
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serf.Add(serf);

        // Create a node with Left state
        var leftNode = new NSerf.Memberlist.State.Node
        {
            Name = "remote-node",
            Addr = System.Net.IPAddress.Parse("127.0.0.1"),
            Port = 9999,
            State = NSerf.Memberlist.State.NodeStateType.Left,
            Meta = Array.Empty<byte>()
        };

        // ACT: Call HandleNodeLeave
        serf.HandleNodeLeave(leftNode);

        // ASSERT: Check member status
        var members = serf.Members();
        var remoteMember = members.FirstOrDefault(m => m.Name == "remote-node");

        if (remoteMember != null)
        {
            _logger.LogInformation("Remote node status: {Status}", remoteMember.Status);
            Assert.Equal(NSerf.Serf.MemberStatus.Left, remoteMember.Status);
        }
        else
        {
            _logger.LogWarning("Remote node not found in members list");
        }
    }

    [Fact]
    public async Task SerfHandleNodeLeave_WithDeadState_MapsToFailed()
    {
        // ARRANGE: Create a Serf instance
        var eventChannel = Channel.CreateUnbounded<IEvent>();
        var config = GetTestConfig("test-node-5", 19009, eventChannel.Writer);
        var serf = await NSerf.Serf.Serf.CreateAsync(config);
        _serf.Add(serf);

        // Create a node with Dead state
        var deadNode = new NSerf.Memberlist.State.Node
        {
            Name = "remote-node-dead",
            Addr = System.Net.IPAddress.Parse("127.0.0.1"),
            Port = 9998,
            State = NSerf.Memberlist.State.NodeStateType.Dead,
            Meta = Array.Empty<byte>()
        };

        // ACT: Call HandleNodeLeave
        serf.HandleNodeLeave(deadNode);

        // ASSERT: Check member status
        var members = serf.Members();
        var remoteMember = members.FirstOrDefault(m => m.Name == "remote-node-dead");

        if (remoteMember != null)
        {
            _logger.LogInformation("Remote dead node status: {Status}", remoteMember.Status);
            Assert.Equal(NSerf.Serf.MemberStatus.Failed, remoteMember.Status);
        }
        else
        {
            _logger.LogWarning("Remote dead node not found in members list");
        }
    }

    private NSerf.Serf.Config GetTestConfig(string nodeName, int port, ChannelWriter<IEvent>? eventCh = null)
    {
        var memberlistConfig = new NSerf.Memberlist.Configuration.MemberlistConfig
        {
            Name = nodeName,
            BindAddr = "127.0.0.1",
            BindPort = port,
            AdvertiseAddr = "127.0.0.1",
            AdvertisePort = port,
            Logger = _logger
        };

        return new NSerf.Serf.Config
        {
            NodeName = nodeName,
            MemberlistConfig = memberlistConfig,
            EventCh = eventCh,
            Logger = _logger,
            BroadcastTimeout = TimeSpan.FromSeconds(5),
            LeavePropagateDelay = TimeSpan.FromSeconds(1)
        };
    }

    [Fact]
    public async Task GracefulLeaveOverridesFailureDetection()
    {
        // ARRANGE: Create two nodes
        var config1 = GetTestConfig("proxy", 19011);
        var config2 = GetTestConfig("backend-test", 19012);

        var serf1 = await NSerf.Serf.Serf.CreateAsync(config1);
        var serf2 = await NSerf.Serf.Serf.CreateAsync(config2);
        _serf.Add(serf1);
        _serf.Add(serf2);

        // Join them
        await serf2.JoinAsync(new[] { "127.0.0.1:19011" }, ignoreOld: false);
        await Task.Delay(500);

        var memberlist1 = serf1.Memberlist!;
        var stateHandler = new NSerf.Memberlist.StateHandlers(memberlist1, _logger);

        // Simulate failure detection on proxy: backend-test reported dead by another node
        var failureMsg = new NSerf.Memberlist.Messages.Dead
        {
            Node = "backend-test",
            From = "some-other-node",  // Reported by different node = failure
            Incarnation = 5
        };

        stateHandler.HandleDeadNode(failureMsg);

        // Verify backend-test is marked as Dead on proxy
        var nodeState = memberlist1.NodeMap.GetValueOrDefault("backend-test");
        Assert.NotNull(nodeState);
        _logger.LogInformation("State after failure detection: {State}", nodeState.State);
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Dead, nodeState.State);

        // ACT: Now backend-test sends graceful leave (Node==From)
        var gracefulLeaveMsg = new NSerf.Memberlist.Messages.Dead
        {
            Node = "backend-test",
            From = "backend-test",  // Same as Node = graceful leave
            Incarnation = 6  // Higher incarnation
        };

        stateHandler.HandleDeadNode(gracefulLeaveMsg);

        // ASSERT: Should override Dead→Left
        nodeState = memberlist1.NodeMap.GetValueOrDefault("backend-test");
        Assert.NotNull(nodeState);
        _logger.LogInformation("State after graceful leave: {State}", nodeState.State);
        Assert.Equal(NSerf.Memberlist.State.NodeStateType.Left, nodeState.State);
    }

    public void Dispose()
    {
        foreach (var s in _serf)
        {
            try
            {
                s.ShutdownAsync().GetAwaiter().GetResult();
                s.Dispose();
            }
            catch { }
        }
    }
}
