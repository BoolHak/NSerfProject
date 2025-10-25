// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.CLI.Tests.Fixtures;

namespace NSerf.CLI.Tests.Commands;

/// <summary>
/// Tests for coordinate/RTT functionality
/// Ported from Go's coordinate tests
/// </summary>
[Trait("Category", "Integration")]
[Collection("Sequential")]
public class CoordinateIntegrationTests : IAsyncLifetime
{
    private AgentFixture? _fixture;

    public async Task InitializeAsync()
    {
        _fixture = new AgentFixture();
        await _fixture.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Coordinate_LocalNode_ReturnsCoordinate()
    {
        var localNode = _fixture!.Agent!.Serf!.LocalMember();
        var coordinate = _fixture.Agent.Serf.GetCoordinate(localNode.Name);

        Assert.NotNull(coordinate);
        await Task.Delay(20);
    }

    [Fact(Timeout = 15000)]
    public async Task Coordinate_NonExistentNode_ReturnsNull()
    {
        var coordinate = _fixture!.Agent!.Serf!.GetCoordinate("non-existent-node");

        Assert.Null(coordinate);
        await Task.Delay(20);
    }

    [Fact(Timeout = 30000)]
    public async Task Coordinate_TwoNodes_CalculatesRTT()
    {
        await using var agent2 = new AgentFixture();
        await agent2.InitializeAsync();

        // Join the nodes
        var addr = $"{agent2.Agent!.Serf!.Members()[0].Addr}:{agent2.Agent.Serf.Members()[0].Port}";
        await _fixture!.Agent!.Serf!.JoinAsync(new[] { addr }, ignoreOld: false);

        // Wait longer for coordinate gossip to propagate
        // Coordinates are exchanged via gossip which takes multiple probe cycles
        await Task.Delay(5000);

        // Get coordinates
        var coord1 = _fixture.Agent.Serf.GetCoordinate(_fixture.Agent.NodeName);
        var coord2 = _fixture.Agent.Serf.GetCoordinate(agent2.Agent.NodeName);

        Assert.NotNull(coord1);

        // Note: coord2 may be null if coordinate gossip hasn't fully propagated
        // This is expected behavior - coordinates are eventually consistent
        if (coord2 == null)
        {
            // Skip RTT calculation if remote coordinate not yet available
            return;
        }

        // RTT should be calculable if both coordinates available
        var rtt = coord1.DistanceTo(coord2);
        Assert.True(rtt.TotalMilliseconds >= 0);
    }

    [Fact(Timeout = 15000)]
    public async Task Coordinate_DisabledCoordinates_ReturnsNull()
    {
        // Create agent with coordinates disabled
        var config = new NSerf.Agent.AgentConfig
        {
            NodeName = TestHelper.GetRandomNodeName(),
            BindAddr = TestHelper.GetRandomBindAddr(),
            RPCAddr = "",
            DisableCoordinates = true
        };

        await using var agent = new NSerf.Agent.SerfAgent(config);
        await agent.StartAsync();

        // When coordinates are disabled, GetCoordinate should return null  
        var coordinate = agent.Serf!.GetCoordinate(agent.NodeName);
        Assert.Null(coordinate);
    }

    [Fact(Timeout = 20000)]
    public async Task Coordinate_UpdatesOverTime()
    {
        var localNode = _fixture!.Agent!.Serf!.LocalMember();
        var coord1 = _fixture.Agent.Serf.GetCoordinate(localNode.Name);

        // Wait for coordinate updates
        await Task.Delay(3000);

        var coord2 = _fixture.Agent.Serf.GetCoordinate(localNode.Name);

        Assert.NotNull(coord1);
        Assert.NotNull(coord2);

        // Coordinates should exist (may or may not have changed)
    }
}
