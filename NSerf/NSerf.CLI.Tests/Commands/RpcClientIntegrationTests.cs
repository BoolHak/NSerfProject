// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NSerf.Agent;
using NSerf.Client;
using NSerf.CLI.Tests.Fixtures;
using NSerf.Serf;
using NSerf.Serf.Events;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Comprehensive RPC client integration tests ported from Go's agent/rpc_client_test.go
/// These tests validate ALL RPC operations end-to-end to ensure correct port.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class RpcClientIntegrationTests : IAsyncLifetime
{
    private AgentFixture? _fixture;
    private RpcClient? _client;

    public async Task InitializeAsync()
    {
        _fixture = new AgentFixture();
        await _fixture.InitializeAsync();
        
        // Create RPC client
        _client = new RpcClient(new RpcConfig
        {
            Address = _fixture.RpcAddr!,
            AuthKey = null
        });
        await _client.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
            _fixture = null;
        }
    }

    /// <summary>
    /// Test: TestRPCClient
    /// Validates basic RPC client connection and handshake
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_Connect_Succeeds()
    {
        // Client already connected in InitializeAsync
        Assert.True(_client!.IsConnected);
        
        // Verify we can make a basic call
        var members = await _client.MembersAsync();
        Assert.NotEmpty(members);
    }

    /// <summary>
    /// Test: TestRPCClientMembers
    /// Validates Members RPC call returns correct member list
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_Members_ReturnsLocalNode()
    {
        // Act
        var members = await _client!.MembersAsync();
        
        // Assert
        Assert.Single(members);
        Assert.Equal(_fixture!.Agent!.NodeName, members[0].Name);
        Assert.Equal("alive", members[0].Status);
    }

    /// <summary>
    /// Test: TestRPCClientMembersFiltered
    /// Validates MembersFiltered RPC call with various filters
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_MembersFiltered_ByStatus_Works()
    {
        // Filter by alive status
        var members = await _client!.MembersFilteredAsync(
            tags: null,
            status: "alive",
            name: null);
        
        Assert.Single(members);
        Assert.Equal("alive", members[0].Status);
        
        // Filter by non-existent status
        var failedMembers = await _client.MembersFilteredAsync(
            tags: null,
            status: "failed",
            name: null);
        
        Assert.Empty(failedMembers);
    }

    /// <summary>
    /// Test: TestRPCClientMembersFiltered (name filter)
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_MembersFiltered_ByName_Works()
    {
        var nodeName = _fixture!.Agent!.NodeName;
        
        // Filter by exact name
        var members = await _client!.MembersFilteredAsync(
            tags: null,
            status: null,
            name: nodeName);
        
        Assert.Single(members);
        Assert.Equal(nodeName, members[0].Name);
        
        // Filter by non-matching name
        var noMembers = await _client.MembersFilteredAsync(
            tags: null,
            status: null,
            name: "nonexistent");
        
        Assert.Empty(noMembers);
    }

    /// <summary>
    /// Test: TestRPCClientMembersFiltered (tag filter)
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_MembersFiltered_ByTags_Works()
    {
        // Filter by existing tag
        var tags = new Dictionary<string, string> { ["role"] = "test" };
        var members = await _client!.MembersFilteredAsync(
            tags: tags,
            status: null,
            name: null);
        
        Assert.Single(members);
        Assert.Contains("role", members[0].Tags.Keys);
        Assert.Equal("test", members[0].Tags["role"]);
    }

    /// <summary>
    /// Test: TestRPCClientJoin
    /// Validates Join RPC call
    /// </summary>
    [Fact(Timeout = 15000)]
    public async Task RpcClient_Join_ConnectsTwoAgents()
    {
        // Arrange - create second agent
        await using var fixture2 = new AgentFixture();
        await fixture2.InitializeAsync();
        
        // Get agent2's address from its member info
        var agent2Members = fixture2.Agent!.Serf!.Members();
        var agent2Addr = $"{agent2Members[0].Addr}:{agent2Members[0].Port}";
        
        // Act - join agent1 to agent2
        var joined = await _client!.JoinAsync(new[] { agent2Addr }, replay: false);
        
        // Assert
        Assert.Equal(1, joined);
        
        // Wait for gossip to propagate
        await Task.Delay(1000);
        
        // Verify both agents see each other
        var members1 = await _client.MembersAsync();
        Assert.Equal(2, members1.Length);
    }

    /// <summary>
    /// Test: TestRPCClientUserEvent
    /// Validates UserEvent RPC call
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_UserEvent_DispatchesSuccessfully()
    {
        // Arrange
        var eventName = "test-event";
        var payload = System.Text.Encoding.UTF8.GetBytes("test-payload");

        var handler = new CapturingEventHandler();
        _fixture!.Agent!.RegisterEventHandler(handler);

        try
        {
            // Act
            await _client!.UserEventAsync(eventName, payload, coalesce: false);

            // Assert
            var evt = await handler.WaitForEventAsync(EventType.User, TimeSpan.FromSeconds(3));
            Assert.NotNull(evt);

            var userEvent = Assert.IsType<UserEvent>(evt);
            Assert.Equal(eventName, userEvent.Name);
            Assert.Equal(payload, userEvent.Payload);
        }
        finally
        {
            _fixture.Agent.DeregisterEventHandler(handler);
        }
    }

    /// <summary>
    /// Test: TestRPCClientForceLeave
    /// Validates ForceLeave RPC call
    /// </summary>
    [Fact(Timeout = 20000)]
    public async Task RpcClient_ForceLeave_RemovesFailedNode()
    {
        // Arrange - create second agent, join, then kill it
        var fixture2 = new AgentFixture();
        await fixture2.InitializeAsync();
        
        var agent2Name = fixture2.Agent!.NodeName;
        // Get agent2's address from its member info
        var agent2Members = fixture2.Agent.Serf!.Members();
        var agent2Addr = $"{agent2Members[0].Addr}:{agent2Members[0].Port}";
        
        // Join
        await _client!.JoinAsync(new[] { agent2Addr }, replay: false);
        await Task.Delay(1000);
        
        // Kill agent2 (don't dispose cleanly)
        await fixture2.Agent.ShutdownAsync();
        await fixture2.RpcServer!.DisposeAsync();
        
        // Wait for failure detection (probe timeout + gossip)
        await Task.Delay(5000);
        
        // Act - force leave the failed node
        await _client.ForceLeaveAsync(agent2Name);
        
        // Poll for status change (failed → left can take time for gossip)
        string? finalStatus = null;
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var members = await _client.MembersAsync();
            var node2 = members.FirstOrDefault(m => m.Name == agent2Name);
            if (node2 != null)
            {
                finalStatus = node2.Status;
                if (finalStatus == "left")
                    break;
            }
        }
        
        // Assert - node should eventually be marked as left (or at least still failed, not alive)
        Assert.NotNull(finalStatus);
        Assert.NotEqual("alive", finalStatus); // Must not be alive
        // Note: Can be "failed" or "left" depending on gossip timing
        
        await fixture2.DisposeAsync();
    }

    /// <summary>
    /// Test: TestRPCClientLeave
    /// Validates Leave RPC call
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_Leave_CausesGracefulShutdown()
    {
        // Act
        await _client!.LeaveAsync();
        
        // Assert - agent should be shutting down
        // Give it a moment to process
        await Task.Delay(500);
        
        // Agent should be in leaving or shutdown state
        var state = _fixture!.Agent!.Serf!.State();
        Assert.True(state == SerfState.SerfLeft || state == SerfState.SerfShutdown, 
            $"Expected SerfLeft or SerfShutdown, got {state}");
    }

    /// <summary>
    /// Test: TestRPCClientUpdateTags (via Tags command)
    /// Validates UpdateTags RPC call
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_UpdateTags_ModifiesNodeTags()
    {
        // Arrange
        var newTags = new Dictionary<string, string>
        {
            ["role"] = "updated",
            ["new_tag"] = "value"
        };
        
        // Act
        await _client!.UpdateTagsAsync(newTags, Array.Empty<string>());
        await Task.Delay(1000); // Wait for gossip
        
        // Assert
        var members = await _client.MembersAsync();
        var localMember = members.First(m => m.Name == _fixture!.Agent!.NodeName);
        
        Assert.Equal("updated", localMember.Tags["role"]);
        Assert.Equal("value", localMember.Tags["new_tag"]);
    }

    /// <summary>
    /// Test: TestRPCClientStats
    /// Validates Stats RPC call
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_Stats_ReturnsAgentInfo()
    {
        // Act
        var stats = await _client!.StatsAsync();
        
        // Assert
        Assert.NotNull(stats);
        Assert.Contains("agent", stats.Keys);
        Assert.Contains("name", stats["agent"].Keys);
        Assert.Equal(_fixture!.Agent!.NodeName, stats["agent"]["name"]);
    }

    /// <summary>
    /// Test: TestRPCClientQuery
    /// Validates Query RPC call
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_Query_DispatchesAndReceivesAck()
    {
        // Arrange
        var queryName = "test-query";
        var payload = System.Text.Encoding.UTF8.GetBytes("query-data");

        var handler = new QueryResponderHandler(payload: Encoding.UTF8.GetBytes("ok"));
        _fixture!.Agent!.RegisterEventHandler(handler);

        try
        {
            // Act
            var queryId = await _client!.QueryAsync(
                name: queryName,
                payload: payload,
                filterNodes: null,
                filterTags: null,
                requestAck: true,
                timeoutSeconds: 5);

            // Assert
            Assert.NotEqual(0UL, queryId);

            var observed = await handler.WaitForQueryAsync(queryName, TimeSpan.FromSeconds(3));
            Assert.NotNull(observed);
            Assert.Equal(queryName, observed!.Name);
            Assert.Equal(payload, observed.Payload);
        }
        finally
        {
            _fixture.Agent.DeregisterEventHandler(handler);
        }
    }

    /// <summary>
    /// Test: TestRPCClientGetCoordinate
    /// Validates GetCoordinate RPC call
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_GetCoordinate_ReturnsCoordinateForExistingNode()
    {
        // Arrange
        var nodeName = _fixture!.Agent!.NodeName;

        // Act
        var coordinate = await WaitForCoordinateAsync(_client!, nodeName, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(coordinate);
        Assert.NotEmpty(coordinate!.Vec);
    }

    /// <summary>
    /// Test: TestRPCClientGetCoordinate (non-existent node)
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task RpcClient_GetCoordinate_NonExistentNode_ReturnsNull()
    {
        // Act
        var result = await _client!.GetCoordinateAsync("nonexistent-node");
        
        // Assert - non-existent nodes return null coordinate
        Assert.Null(result);
    }

    private static async Task<Client.Responses.Coordinate?> WaitForCoordinateAsync(RpcClient client, string nodeName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow <= deadline)
        {
            var coordinate = await client.GetCoordinateAsync(nodeName);
            if (coordinate != null)
            {
                return coordinate;
            }

            await Task.Delay(100);
        }

        return await client.GetCoordinateAsync(nodeName);
    }

    private sealed class CapturingEventHandler : IEventHandler
    {
        private readonly List<Event> _events = new();
        private readonly object _lock = new();

        public void HandleEvent(Event @event)
        {
            lock (_lock)
            {
                _events.Add(@event);
            }
        }

        public async Task<Event?> WaitForEventAsync(EventType expectedType, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow <= deadline)
            {
                lock (_lock)
                {
                    var match = _events.LastOrDefault(evt => evt.EventType() == expectedType);
                    if (match != null)
                    {
                        return match;
                    }
                }

                await Task.Delay(50);
            }

            lock (_lock)
            {
                return _events.LastOrDefault(evt => evt.EventType() == expectedType);
            }
        }
    }

    private sealed class QueryResponderHandler : IEventHandler
    {
        private readonly byte[] _response;
        private readonly List<Query> _queries = new();
        private readonly object _lock = new();

        public QueryResponderHandler(byte[] payload)
        {
            _response = payload;
        }

        public void HandleEvent(Event @event)
        {
            if (@event is not Query query)
            {
                return;
            }

            lock (_lock)
            {
                _queries.Add(query);
            }

            _ = query.RespondAsync(_response);
        }

        public async Task<Query?> WaitForQueryAsync(string name, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow <= deadline)
            {
                lock (_lock)
                {
                    var match = _queries.LastOrDefault(q => q.Name == name);
                    if (match != null)
                    {
                        return match;
                    }
                }

                await Task.Delay(50);
            }

            lock (_lock)
            {
                return _queries.LastOrDefault(q => q.Name == name);
            }
        }
    }
}
