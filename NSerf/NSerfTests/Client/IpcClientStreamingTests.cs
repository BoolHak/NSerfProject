using NSerf.Client;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using System.Threading.Channels;

namespace NSerfTests.Client;

public class IpcClientStreamingTests : IAsyncDisposable
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
    public async Task Client_CanStartMonitorStream()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var logChannel = Channel.CreateUnbounded<string>();
        var streamHandle = await client.MonitorAsync("debug", logChannel.Writer, 2, CancellationToken.None);

        Assert.NotNull(streamHandle);
        Assert.Equal(2ul, streamHandle.Seq);
    }

    [Fact]
    public async Task Client_CanStartEventStream()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var streamHandle = await client.StreamAsync("user", eventChannel.Writer, 2, CancellationToken.None);

        Assert.NotNull(streamHandle);
        Assert.Equal(2ul, streamHandle.Seq);
    }

    [Fact]
    public async Task Client_CanStopStream()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var logChannel = Channel.CreateUnbounded<string>();
        var streamHandle = await client.MonitorAsync("debug", logChannel.Writer, 2, CancellationToken.None);

        var stopResponse = await client.StopAsync(streamHandle.Seq, 3, CancellationToken.None);

        Assert.Equal(3ul, stopResponse.Seq);
        Assert.Equal("", stopResponse.Error);
    }

    [Fact]
    public async Task Client_MultipleStreams_CanCoexist()
    {
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var logChannel = Channel.CreateUnbounded<string>();
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();

        var monitorHandle = await client.MonitorAsync("debug", logChannel.Writer, 2, CancellationToken.None);
        var streamHandle = await client.StreamAsync("user", eventChannel.Writer, 3, CancellationToken.None);

        Assert.Equal(2ul, monitorHandle.Seq);
        Assert.Equal(3ul, streamHandle.Seq);

        // Stop both
        await client.StopAsync(monitorHandle.Seq, 4, CancellationToken.None);
        await client.StopAsync(streamHandle.Seq, 5, CancellationToken.None);
    }
}
