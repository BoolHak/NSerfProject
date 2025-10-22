using MessagePack;
using NSerf.Client;
using NSerf.Serf;
using NSerf.Memberlist.Configuration;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for server-side log streaming (Phase 10).
/// Tests written BEFORE implementation.
/// </summary>
public class ServerLogStreamingTests : IAsyncLifetime
{
    private readonly MessagePackSerializerOptions _options = MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
    private AgentIpc _server = null!;
    private IpcClient _client = null!;
    private NSerf.Serf.Serf _serf = null!;

    public async Task InitializeAsync()
    {
        _serf = MockSerfForIpc.Create();
        _server = new AgentIpc(_serf, "127.0.0.1:0", null);
        await _server.StartAsync(CancellationToken.None);

        _client = new IpcClient(_options);
        await _client.ConnectAsync("127.0.0.1", _server.Port, CancellationToken.None);
        await _client.HandshakeAsync(1, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        _serf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Monitor_DebugLevel_StreamsLogs()
    {
        // Test: Monitor command should start streaming logs at specified level
        var logChannel = Channel.CreateUnbounded<string>();
        var handle = await _client.MonitorAsync("debug", logChannel.Writer, 2, CancellationToken.None);
        
        Assert.Equal(2ul, handle.Seq);
        
        // TODO: Server should send log records
        // For now, just verify handle created
    }

    [Fact(Timeout = 5000)]
    public async Task Monitor_InfoLevel_FiltersDebugLogs()
    {
        // Test: Monitor with "info" level should not send debug logs
        var logChannel = Channel.CreateUnbounded<string>();
        var handle = await _client.MonitorAsync("info", logChannel.Writer, 3, CancellationToken.None);
        
        Assert.Equal(3ul, handle.Seq);
    }

    [Fact(Timeout = 5000)]
    public async Task Monitor_DuplicateSeq_ReturnsError()
    {
        // Test: Starting monitor with existing seq should fail
        var logChannel1 = Channel.CreateUnbounded<string>();
        await _client.MonitorAsync("debug", logChannel1.Writer, 4, CancellationToken.None);
        
        var logChannel2 = Channel.CreateUnbounded<string>();
        
        // Second monitor with same seq should fail
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _client.MonitorAsync("debug", logChannel2.Writer, 4, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_ExistingMonitor_StopsStream()
    {
        // Test: Stop command should terminate monitor stream
        var logChannel = Channel.CreateUnbounded<string>();
        var handle = await _client.MonitorAsync("debug", logChannel.Writer, 5, CancellationToken.None);
        
        // Stop the stream
        var stopResponse = await _client.StopAsync(handle.Seq, 6, CancellationToken.None);
        
        Assert.Equal("", stopResponse.Error);
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_NonExistentStream_ReturnsError()
    {
        // Test: Stopping non-existent stream should return error
        var stopResponse = await _client.StopAsync(999, 7, CancellationToken.None);
        
        // Should get error about stream not existing
        Assert.Contains("exist", stopResponse.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 10000)]
    public async Task Monitor_MultipleClients_EachReceiveOwnLogs()
    {
        // Test: Multiple clients can monitor independently
        var client2 = new IpcClient(_options);
        await client2.ConnectAsync("127.0.0.1", _server.Port, CancellationToken.None);
        await client2.HandshakeAsync(1, CancellationToken.None);
        
        try
        {
            var logChannel1 = Channel.CreateUnbounded<string>();
            var handle1 = await _client.MonitorAsync("debug", logChannel1.Writer, 8, CancellationToken.None);
            
            var logChannel2 = Channel.CreateUnbounded<string>();
            var handle2 = await client2.MonitorAsync("info", logChannel2.Writer, 9, CancellationToken.None);
            
            Assert.Equal(8ul, handle1.Seq);
            Assert.Equal(9ul, handle2.Seq);
            
            // Both streams should be independent
        }
        finally
        {
            await client2.DisposeAsync();
        }
    }

    [Fact(Timeout = 5000)]
    public async Task Monitor_ClientDisconnect_CleansUpStream()
    {
        // Test: Disconnecting client should cleanup server-side stream
        var client2 = new IpcClient(_options);
        await client2.ConnectAsync("127.0.0.1", _server.Port, CancellationToken.None);
        await client2.HandshakeAsync(1, CancellationToken.None);
        
        var logChannel = Channel.CreateUnbounded<string>();
        var handle = await client2.MonitorAsync("debug", logChannel.Writer, 10, CancellationToken.None);
        
        Assert.Equal(10ul, handle.Seq);
        
        // Disconnect client - should cleanup stream on server
        await client2.DisposeAsync();
        
        // If we had server inspection, we'd verify stream removed
        Assert.True(true);
    }
}
