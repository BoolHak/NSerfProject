using NSerf.Client;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for Stats and Coordinate command handlers.
/// Phase 4: RED phase - write tests before implementation.
/// </summary>
public class StatsCoordinateTests : IAsyncDisposable
{
    private readonly List<AgentIpc> _servers = new();
    private readonly List<IpcClient> _clients = new();
    private readonly List<NSerf.Serf.Serf> _serfInstances = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }
        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
        foreach (var serf in _serfInstances)
        {
            serf.Dispose();
        }
    }

    private AgentIpc CreateServer(string? authKey = null)
    {
        var nodeName = $"test-node-{Guid.NewGuid()}";
        var config = new Config
        {
            NodeName = nodeName,
            MemberlistConfig = new MemberlistConfig
            {
                Name = nodeName,
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };
        var serf = NSerf.Serf.Serf.CreateAsync(config).GetAwaiter().GetResult();
        _serfInstances.Add(serf);
        var server = new AgentIpc(serf, "127.0.0.1:0", authKey);
        _servers.Add(server);
        return server;
    }

    private IpcClient CreateClient()
    {
        var client = new IpcClient();
        _clients.Add(client);
        return client;
    }

    [Fact(Timeout = 20000)]
    public async Task Stats_ReturnsValidMemberlistStats()
    {
        // RED: Test that stats command returns real memberlist statistics
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var (header, stats) = await client.GetStatsAsync(2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(stats);

        // Should have memberlist section
        Assert.True(stats.ContainsKey("memberlist"));
        var memberlistStats = stats["memberlist"];

        // Should contain basic counters
        Assert.True(memberlistStats.ContainsKey("msg_alive"));
        Assert.True(memberlistStats.ContainsKey("msg_dead"));
        Assert.True(memberlistStats.ContainsKey("msg_suspect"));
    }

    [Fact(Timeout = 20000)]
    public async Task Stats_IncludesSerfAgentInfo()
    {
        // RED: Test that stats include Serf-specific information
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var (header, stats) = await client.GetStatsAsync(2, CancellationToken.None);

        Assert.Equal("", header.Error);

        // Should have agent section
        Assert.True(stats.ContainsKey("agent"));
        var agentStats = stats["agent"];

        // Should contain agent name
        Assert.True(agentStats.ContainsKey("name"));
    }

    [Fact(Timeout = 20000)]
    public async Task GetCoordinate_LocalNode_ReturnsCoordinate()
    {
        // RED: Test coordinate retrieval for local node
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        // Get local node name from members
        var (_, membersBody) = await client.GetMembersAsync(2, CancellationToken.None);
        var localNodeName = membersBody.Members[0].Name;

        // Request coordinate for local node
        // TODO: Need to add GetCoordinateAsync to IpcClient
        // var (header, coord) = await client.GetCoordinateAsync(localNodeName, 3, CancellationToken.None);

        // Assert.Equal(3ul, header.Seq);
        // Assert.Equal("", header.Error);
        // Assert.NotNull(coord);
    }

    [Fact(Timeout = 20000)]
    public async Task GetCoordinate_NonExistentNode_ReturnsNull()
    {
        // RED: Test coordinate retrieval for non-existent node
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        // TODO: Need to add GetCoordinateAsync to IpcClient
        // var (header, coord) = await client.GetCoordinateAsync("non-existent-node", 2, CancellationToken.None);

        // Assert.Equal(2ul, header.Seq);
        // Assert.Equal("", header.Error);
        // Assert.Null(coord);
    }
}
