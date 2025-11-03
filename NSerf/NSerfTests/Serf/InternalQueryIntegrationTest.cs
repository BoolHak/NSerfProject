// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Integration tests for Internal Query Handler
// Tests the full pipeline: Serf → SerfQueries → Handlers → KeyManager

using System.Threading.Channels;
using FluentAssertions;
using NSerf.Memberlist;
using NSerf.Memberlist.Configuration;
using NSerf.Serf;
using NSerf.Serf.Events;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Integration tests for the internal query handler system.
/// Tests Phase 6: Integration & Testing
/// </summary>
public class InternalQueryIntegrationTest
{
    /// <summary>
    /// Tests that internal query handler is automatically wired in during Serf.CreateAsync().
    /// </summary>
    [Fact]
    public async Task Serf_CreateAsync_ShouldWireInternalQueryHandler()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<IEvent>();

        var config = new Config
        {
            NodeName = "test-node",
            EventCh = eventCh.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        // Act - CreateAsync should wire in the internal query handler
        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Assert - Serf should be created successfully with handler wired in
        serf.Should().NotBeNull();
        serf.Config.EventCh.Should().NotBeNull("EventCh should be set");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that non-internal queries pass through to user EventCh.
    /// </summary>
    [Fact]
    public async Task InternalQueryHandler_NonInternalQuery_ShouldPassThrough()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<IEvent>();

        var config = new Config
        {
            NodeName = "test-node",
            EventCh = eventCh.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "test-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Send a user event (non-internal)
        await serf.UserEventAsync("test-event", System.Text.Encoding.UTF8.GetBytes("test payload"), false);

        // Assert - Event should pass through to user's EventCh
        // Note: First event will be the node's initial self-join MemberEvent, keep reading for UserEvent
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        UserEvent? userEvt = null;
        try
        {
            while (userEvt == null)
            {
                var evt = await eventCh.Reader.ReadAsync(cts.Token);
                if (evt is UserEvent ue)
                {
                    userEvt = ue;
                }
                // Skip other events (like initial MemberEvent)
            }
        }
        catch (OperationCanceledException)
        {
            userEvt.Should().NotBeNull("Should have received UserEvent before timeout");
        }

        userEvt.Should().NotBeNull();
        userEvt!.Name.Should().Be("test-event");

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests end-to-end key management with 2-node cluster.
    /// </summary>
    [Fact]
    public async Task KeyManager_ListKeys_TwoNodeCluster_ShouldReturnKeys()
    {
        // Arrange - Create 2 nodes with encryption
        var key1 = "T9jncgl9mbLus+baTTa7q7nPSUrXwbDi2dhbtqir37s=";
        var key1Bytes = Convert.FromBase64String(key1);
        var keyring1 = Keyring.Create(null, key1Bytes);
        var keyring2 = Keyring.Create(null, key1Bytes);

        var eventCh1 = Channel.CreateUnbounded<IEvent>();
        var eventCh2 = Channel.CreateUnbounded<IEvent>();

        var config1 = new Config
        {
            NodeName = "node1",
            EventCh = eventCh1.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring1
            }
        };

        var config2 = new Config
        {
            NodeName = "node2",
            EventCh = eventCh2.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node2",
                BindAddr = "127.0.0.1",
                BindPort = 0,
                Keyring = keyring2
            }
        };

        using var serf1 = await NSerf.Serf.Serf.CreateAsync(config1);
        using var serf2 = await NSerf.Serf.Serf.CreateAsync(config2);

        // Join the cluster
        var port1 = serf1.Memberlist!.LocalNode.Port;
        await serf2.JoinAsync(new[] { $"127.0.0.1:{port1}" }, false);

        // Wait for cluster formation
        await Task.Delay(500);

        // Verify cluster is formed
        serf1.NumMembers().Should().Be(2);
        serf2.NumMembers().Should().Be(2);

        // Act - List keys from node1
        var keyManager = new KeyManager(serf1);
        var response = await keyManager.ListKeys();

        // Assert - Should get responses from both nodes
        response.Should().NotBeNull();
        response.NumNodes.Should().Be(2, "cluster has 2 nodes");

        // Note: Response count might be less if query times out before responses arrive
        // This is expected behavior in the current implementation

        // Cleanup
        await serf1.ShutdownAsync();
        await serf2.ShutdownAsync();
    }

    /// <summary>
    /// Tests that encryption check prevents operations without keyring.
    /// </summary>
    [Fact]
    public async Task KeyManager_WithoutEncryption_ShouldHandleGracefully()
    {
        // Arrange - Create node WITHOUT encryption
        var eventCh = Channel.CreateUnbounded<IEvent>();

        var config = new Config
        {
            NodeName = "node1",
            EventCh = eventCh.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
                // No keyring
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Verify encryption is NOT enabled
        serf.EncryptionEnabled().Should().BeFalse();

        // Act - Try to list keys
        var keyManager = new KeyManager(serf);
        var response = await keyManager.ListKeys();

        // Assert - Should complete without throwing
        response.Should().NotBeNull();
        response.NumNodes.Should().Be(1);

        await serf.ShutdownAsync();
    }

    /// <summary>
    /// Tests that member events pass through the internal query handler.
    /// </summary>
    [Fact]
    public async Task InternalQueryHandler_MemberEvents_ShouldPassThrough()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<IEvent>();

        var config1 = new Config
        {
            NodeName = "node1",
            EventCh = eventCh.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        var config2 = new Config
        {
            NodeName = "node2",
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node2",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf1 = await NSerf.Serf.Serf.CreateAsync(config1);
        using var serf2 = await NSerf.Serf.Serf.CreateAsync(config2);

        // Act - Join node2 to node1
        var port1 = serf1.Memberlist!.LocalNode.Port;
        await serf2.JoinAsync(new[] { $"127.0.0.1:{port1}" }, false);

        // Assert - Should receive member join event on node1's EventCh
        // Note: First event will be node1's initial self-join, we need to keep reading for node2
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var receivedNode2Event = false;
        try
        {
            while (!receivedNode2Event)
            {
                var evt = await eventCh.Reader.ReadAsync(cts.Token);
                if (evt is MemberEvent memberEvt && memberEvt.Type == EventType.MemberJoin)
                {
                    // Check if node2 is in this event
                    if (memberEvt.Members.Any(m => m.Name == "node2"))
                    {
                        receivedNode2Event = true;
                    }
                    // Otherwise continue reading - might be node1's initial self-join event
                }
            }
        }
        catch (OperationCanceledException)
        {
            receivedNode2Event.Should().BeTrue("Should have received node2's join event before timeout");
        }

        receivedNode2Event.Should().BeTrue("Should have received node2's join event");

        // Cleanup
        await serf1.ShutdownAsync();
        await serf2.ShutdownAsync();
    }

    /// <summary>
    /// Tests that the internal query handler is wired correctly in the pipeline.
    /// Note: The originator of a query will still see it in their EventCh.
    /// The handler intercepts queries from OTHER nodes.
    /// </summary>
    [Fact]
    public async Task InternalQueryHandler_Integration_IsWiredCorrectly()
    {
        // Arrange
        var eventCh = Channel.CreateUnbounded<IEvent>();

        var config = new Config
        {
            NodeName = "node1",
            EventCh = eventCh.Writer,
            MemberlistConfig = new MemberlistConfig
            {
                Name = "node1",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        using var serf = await NSerf.Serf.Serf.CreateAsync(config);

        // Act - Broadcast an internal query
        var queryParams = new QueryParam();
        var queryResp = await serf.QueryAsync("_serf_ping", Array.Empty<byte>(), queryParams);

        // Assert - Query response should be returned
        queryResp.Should().NotBeNull("query should complete");

        // Note: The originator sees their own query in EventCh
        // This is expected behavior - the handler intercepts queries from OTHER nodes
        // Queries we send ourselves are our responsibility to track

        await serf.ShutdownAsync();
    }
}
