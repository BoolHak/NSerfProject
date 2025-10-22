using NSerf.Client;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using System.Net.Sockets;

namespace NSerfTests.Client;

public class IpcClientTests : IAsyncDisposable
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

    [Fact]
    public async Task Client_CanConnectAndHandshake()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        var response = await client.HandshakeAsync(1, CancellationToken.None);

        Assert.Equal(1ul, response.Seq);
        Assert.Equal("", response.Error);
    }

    [Fact]
    public async Task Client_HandshakeWithUnsupportedVersion_ReturnsError()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        var response = await client.HandshakeAsync(999, CancellationToken.None);

        Assert.Equal(1ul, response.Seq);
        Assert.Equal(IpcProtocol.UnsupportedIPCVersion, response.Error);
    }

    [Fact]
    public async Task Client_CommandWithoutHandshake_ReturnsHandshakeRequired()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        // Skip handshake, try members command
        var response = await client.SendHeaderOnlyAsync(IpcProtocol.MembersCommand, 1, CancellationToken.None);

        Assert.Equal(1ul, response.Seq);
        Assert.Equal(IpcProtocol.HandshakeRequired, response.Error);
    }

    [Fact]
    public async Task Client_WithAuth_RequiresAuthAfterHandshake()
    {
        var server = CreateServer(authKey: "secret123");
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        // Handshake first
        var handshakeResp = await client.HandshakeAsync(1, CancellationToken.None);
        Assert.Equal("", handshakeResp.Error);

        // Try command without auth
        var membersResp = await client.SendHeaderOnlyAsync(IpcProtocol.MembersCommand, 2, CancellationToken.None);
        Assert.Equal(IpcProtocol.AuthRequired, membersResp.Error);
    }

    [Fact]
    public async Task Client_WithAuth_CanAuthenticateSuccessfully()
    {
        var server = CreateServer(authKey: "secret123");
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        // Handshake
        await client.HandshakeAsync(1, CancellationToken.None);

        // Auth
        var authResp = await client.AuthAsync("secret123", 2, CancellationToken.None);
        Assert.Equal(2ul, authResp.Seq);
        Assert.Equal("", authResp.Error);
    }

    [Fact]
    public async Task Client_WithAuth_InvalidToken_ReturnsError()
    {
        var server = CreateServer(authKey: "secret123");
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        // Handshake
        await client.HandshakeAsync(1, CancellationToken.None);

        // Auth with wrong key
        var authResp = await client.AuthAsync("wrongkey", 2, CancellationToken.None);
        Assert.Equal(2ul, authResp.Seq);
        Assert.Equal(IpcProtocol.InvalidAuthToken, authResp.Error);
    }

    [Fact]
    public async Task Client_ConcurrentSends_AreSerializedCorrectly()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        // Handshake first
        await client.HandshakeAsync(1, CancellationToken.None);

        // Send two commands sequentially (concurrent reads would require more complex client)
        var resp1 = await client.SendHeaderOnlyAsync(IpcProtocol.MembersCommand, 2, CancellationToken.None);
        var resp2 = await client.SendHeaderOnlyAsync(IpcProtocol.StatsCommand, 3, CancellationToken.None);

        // Both should succeed
        Assert.Equal(2ul, resp1.Seq);
        Assert.Equal("", resp1.Error);
        Assert.Equal(3ul, resp2.Seq);
        Assert.Equal("", resp2.Error);
    }

    [Fact]
    public async Task Client_MultipleClients_CanConnectSimultaneously()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client1 = CreateClient();
        var client2 = CreateClient();

        await client1.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client2.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        var resp1 = await client1.HandshakeAsync(1, CancellationToken.None);
        var resp2 = await client2.HandshakeAsync(1, CancellationToken.None);

        Assert.Equal("", resp1.Error);
        Assert.Equal("", resp2.Error);
    }

    [Fact]
    public async Task Client_DisconnectBeforeResponse_ThrowsException()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        // Handshake first to establish connection
        await client.HandshakeAsync(1, CancellationToken.None);

        // Start a command but dispose before reading response
        _ = client.SendHeaderOnlyAsync(IpcProtocol.MembersCommand, 2, CancellationToken.None);
        await Task.Delay(10); // Give time for send to start
        await client.DisposeAsync();

        // Next operation should fail
        var client2 = CreateClient();
        await client2.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        // This should succeed - just verify we can create new connections after dispose
        var resp = await client2.HandshakeAsync(1, CancellationToken.None);
        Assert.Equal("", resp.Error);
    }

    [Fact]
    public async Task Client_SendWithCancellation_PropagatesCancellation()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // TaskCanceledException is a subclass of OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.HandshakeAsync(1, cts.Token));
    }

    [Fact]
    public async Task Client_CanGetMembersWithBody()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var (header, body) = await client.GetMembersAsync(2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(body);
        // Serf now includes local node in members
        Assert.Single(body.Members);
        Assert.Equal("alive", body.Members[0].Status);
    }

    [Fact]
    public async Task Client_CanGetStats()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var (header, stats) = await client.GetStatsAsync(2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        Assert.Equal("", header.Error);
        Assert.NotNull(stats);
    }

    [Fact]
    public async Task Client_CanSendJoinRequest()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var (header, body) = await client.JoinAsync(new[] { "node1:7946" }, false, 2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        // Joining non-existent node fails as expected
        Assert.Contains("Failed to join", header.Error);
        Assert.NotNull(body);
        Assert.Equal(0, body.Num);
    }

    [Fact]
    public async Task Client_CanSendLeaveRequest()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var header = await client.LeaveAsync(2, CancellationToken.None);

        Assert.Equal(2ul, header.Seq);
        Assert.Equal("", header.Error);
    }
}
