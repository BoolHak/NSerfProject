using MessagePack;
using NSerf.Client;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace NSerfTests.Client;

public class AgentIpcServerTests : IDisposable
{
    private readonly List<AgentIpc> _servers = new();
    private readonly MessagePackSerializerOptions _serializerOptions = MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.DisposeAsync().AsTask().Wait();
        }
    }

    private AgentIpc CreateServer(string? authKey = null)
    {
        var mockSerf = new MockSerf();
        var server = new AgentIpc(mockSerf, "127.0.0.1:0", authKey);
        _servers.Add(server);
        return server;
    }

    [Fact]
    public async Task Server_StartsAndAcceptsConnection()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        Assert.True(server.Port > 0);

        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.Port);

        Assert.True(client.Connected);
    }

    [Fact]
    public async Task Server_WithoutHandshake_RejectsCommands()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", server.Port, cts.Token);
        var stream = tcpClient.GetStream();

        // Give server time to start handler
        await Task.Delay(200, cts.Token);

        // Send members command WITHOUT handshake
        var header = new RequestHeader { Command = IpcProtocol.MembersCommand, Seq = 1 };
        Console.WriteLine("[Test] Serializing request...");
        await MessagePackSerializer.SerializeAsync(stream, header, _serializerOptions, cts.Token);
        Console.WriteLine("[Test] Flushing...");
        await stream.FlushAsync(cts.Token);
        Console.WriteLine($"[Test] Request sent, stream CanWrite={stream.CanWrite}, CanRead={stream.CanRead}");

        Console.WriteLine("[Test] Waiting for response...");
        // Use MessagePackStreamReader for proper bidirectional communication
        using var reader = new MessagePackStreamReader(stream, leaveOpen: true);
        var msgpack = await reader.ReadAsync(cts.Token);
        if (!msgpack.HasValue)
        {
            throw new Exception("No response received from server");
        }
        var response = MessagePackSerializer.Deserialize<ResponseHeader>(msgpack.Value, _serializerOptions);
        Console.WriteLine($"[Test] Response received, stream CanWrite={stream.CanWrite}, CanRead={stream.CanRead}");

        Assert.Equal(1ul, response.Seq);
        Assert.Equal(IpcProtocol.HandshakeRequired, response.Error);
    }

    [Fact]
    public async Task Server_WithAuthKey_RequiresAuthAfterHandshake()
    {
        var server = CreateServer(authKey: "secret123");
        await server.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", server.Port, cts.Token);
        var stream = tcpClient.GetStream();
        await Task.Delay(50, cts.Token);

        // Perform handshake
        var handshakeReq = new RequestHeader { Command = IpcProtocol.HandshakeCommand, Seq = 1 };
        await MessagePackSerializer.SerializeAsync(stream, handshakeReq, _serializerOptions, cts.Token);
        var handshakeBody = new HandshakeRequest { Version = 1 };
        await MessagePackSerializer.SerializeAsync(stream, handshakeBody, _serializerOptions, cts.Token);
        await stream.FlushAsync(cts.Token);

        using var reader1 = new MessagePackStreamReader(stream, leaveOpen: true);
        var msgpack1 = await reader1.ReadAsync(cts.Token);
        var handshakeResp = MessagePackSerializer.Deserialize<ResponseHeader>(msgpack1!.Value, _serializerOptions);
        Assert.Equal("", handshakeResp.Error);

        // Try command without auth
        var membersReq = new RequestHeader { Command = IpcProtocol.MembersCommand, Seq = 2 };
        await MessagePackSerializer.SerializeAsync(stream, membersReq, _serializerOptions, cts.Token);
        await stream.FlushAsync(cts.Token);

        using var reader2 = new MessagePackStreamReader(stream, leaveOpen: true);
        var msgpack2 = await reader2.ReadAsync(cts.Token);
        var membersResp = MessagePackSerializer.Deserialize<ResponseHeader>(msgpack2!.Value, _serializerOptions);

        Assert.Equal(IpcProtocol.AuthRequired, membersResp.Error);
    }

    [Fact]
    public async Task Server_UnsupportedCommand_ReturnsError()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync("127.0.0.1", server.Port, cts.Token);
        var stream = tcpClient.GetStream();
        await Task.Delay(50, cts.Token);

        // Perform handshake first
        var handshakeReq = new RequestHeader { Command = IpcProtocol.HandshakeCommand, Seq = 1 };
        await MessagePackSerializer.SerializeAsync(stream, handshakeReq, _serializerOptions, cts.Token);
        var handshakeBody = new HandshakeRequest { Version = 1 };
        await MessagePackSerializer.SerializeAsync(stream, handshakeBody, _serializerOptions, cts.Token);
        await stream.FlushAsync(cts.Token);
        using var reader3 = new MessagePackStreamReader(stream, leaveOpen: true);
        await reader3.ReadAsync(cts.Token);

        // Send invalid command
        var invalidReq = new RequestHeader { Command = "invalid-command", Seq = 2 };
        await MessagePackSerializer.SerializeAsync(stream, invalidReq, _serializerOptions, cts.Token);
        await stream.FlushAsync(cts.Token);

        using var reader4 = new MessagePackStreamReader(stream, leaveOpen: true);
        var msgpack4 = await reader4.ReadAsync(cts.Token);
        var response = MessagePackSerializer.Deserialize<ResponseHeader>(msgpack4!.Value, _serializerOptions);

        Assert.Equal(IpcProtocol.UnsupportedCommand, response.Error);
    }
}

// Mock Serf for testing
public class MockSerf
{
}
