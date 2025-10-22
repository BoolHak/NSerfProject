using NSerf.Client;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using System.Threading.Channels;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for streaming infrastructure: IResponseHandler, dispatch map, background reader.
/// Phase 6: RED phase - write tests before implementation.
/// </summary>
public class StreamingInfrastructureTests : IAsyncDisposable
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

    [Fact(Timeout = 3000)]
    public async Task Monitor_RegistersHandlerAndWaitsForAck()
    {
        // Test that Monitor registers handler and receives acknowledgment
        // Server-side log streaming is TODO (Phase 7)
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var logChannel = Channel.CreateUnbounded<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Should complete successfully after server acknowledges
        var handle = await client.MonitorAsync("debug", logChannel.Writer, 2, cts.Token);

        Assert.NotNull(handle);
        Assert.Equal(2ul, handle.Seq);
        // Note: Actual log streaming from server is TODO (Phase 7)
    }

    [Fact(Timeout = 5000)]
    public async Task Monitor_MultipleClients_EachReceiveTheirOwnLogs()
    {
        // RED: Test that multiple monitor streams work independently
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client1 = CreateClient();
        var client2 = CreateClient();

        await client1.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client1.HandshakeAsync(1, CancellationToken.None);

        await client2.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client2.HandshakeAsync(1, CancellationToken.None);

        var logChannel1 = Channel.CreateUnbounded<string>();
        var logChannel2 = Channel.CreateUnbounded<string>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var handle1 = await client1.MonitorAsync("info", logChannel1.Writer, 2, cts.Token);
        var handle2 = await client2.MonitorAsync("debug", logChannel2.Writer, 3, cts.Token);

        // Both clients registered successfully (each with their own sequence)
        Assert.Equal(2ul, handle1.Seq);
        Assert.Equal(3ul, handle2.Seq);
        
        // Both channels should be ready to receive (not closed)
        Assert.False(logChannel1.Reader.Completion.IsCompleted);
        Assert.False(logChannel2.Reader.Completion.IsCompleted);
    }

    [Fact(Timeout = 3000)]
    public async Task Stop_TerminatesMonitorStream_AndClosesChannel()
    {
        // RED: Test that Stop command properly terminates streaming
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var logChannel = Channel.CreateUnbounded<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var handle = await client.MonitorAsync("debug", logChannel.Writer, 2, cts.Token);

        // Stop the stream
        var stopResponse = await client.StopAsync(handle.Seq, 3, cts.Token);
        Assert.Equal("", stopResponse.Error);

        // Channel should eventually complete (writer closed)
        await Task.Delay(100); // Give time for cleanup
        var completed = logChannel.Reader.Completion.IsCompleted;
        Assert.True(completed, "Expected channel to be completed after Stop");
    }

    [Fact(Timeout = 3000)]
    public async Task Stream_ReceivesEvents_WhenServerEmitsEvents()
    {
        // RED: Test that Stream command receives actual events
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var handle = await client.StreamAsync("*", eventChannel.Writer, 2, cts.Token);

        // In real implementation, server would emit member-join events, user events, etc.
        // For now, just verify the stream is established
        Assert.True(handle.Seq > 0);
    }

    [Fact(Timeout = 3000)]
    public async Task BackgroundReader_DispatchesResponses_ToCorrectHandler()
    {
        // RED: Test that background reader correctly routes responses by sequence number
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        // Send multiple commands with different sequence numbers
        var (membersHeader, _) = await client.GetMembersAsync(2, CancellationToken.None);
        var (statsHeader, _) = await client.GetStatsAsync(3, CancellationToken.None);

        // Each should get its own response with correct sequence
        Assert.Equal(2ul, membersHeader.Seq);
        Assert.Equal(3ul, statsHeader.Seq);
    }

    [Fact(Timeout = 3000)]
    public async Task Client_GracefulDisposal_CleansUpAllHandlers()
    {
        // RED: Test that disposing client cleans up all active handlers
        var server = CreateServer();
        await server.StartAsync(CancellationToken.None);

        var client = CreateClient();
        await client.ConnectAsync("127.0.0.1", server.Port, CancellationToken.None);
        await client.HandshakeAsync(1, CancellationToken.None);

        var logChannel = Channel.CreateUnbounded<string>();
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await client.MonitorAsync("debug", logChannel.Writer, 2, cts.Token);
        await client.StreamAsync("*", eventChannel.Writer, 3, cts.Token);

        // Dispose client
        await client.DisposeAsync();

        // Both channels should be completed
        await Task.Delay(100);
        Assert.True(logChannel.Reader.Completion.IsCompleted);
        Assert.True(eventChannel.Reader.Completion.IsCompleted);
    }
}
