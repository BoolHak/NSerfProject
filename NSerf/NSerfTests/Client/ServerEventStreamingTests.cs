using MessagePack;
using NSerf.Client;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// TDD tests for server-side event streaming (Phase 11).
/// Tests written BEFORE implementation.
/// </summary>
public class ServerEventStreamingTests : IAsyncLifetime
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
    public async Task Stream_UserEventFilter_CreatesStream()
    {
        // Test: Stream command with "user" filter should start event stream
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var handle = await _client.StreamAsync("user", eventChannel.Writer, 2, CancellationToken.None);
        
        Assert.Equal(2ul, handle.Seq);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_MemberJoinFilter_CreatesStream()
    {
        // Test: Stream command with "member-join" filter
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var handle = await _client.StreamAsync("member-join", eventChannel.Writer, 3, CancellationToken.None);
        
        Assert.Equal(3ul, handle.Seq);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_WildcardFilter_CreatesStream()
    {
        // Test: Stream with "*" (all events)
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var handle = await _client.StreamAsync("*", eventChannel.Writer, 4, CancellationToken.None);
        
        Assert.Equal(4ul, handle.Seq);
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_DuplicateSeq_ReturnsError()
    {
        // Test: Starting stream with existing seq should fail
        var eventChannel1 = Channel.CreateUnbounded<Dictionary<string, object>>();
        await _client.StreamAsync("user", eventChannel1.Writer, 5, CancellationToken.None);
        
        var eventChannel2 = Channel.CreateUnbounded<Dictionary<string, object>>();
        
        // Second stream with same seq should fail
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _client.StreamAsync("user", eventChannel2.Writer, 5, CancellationToken.None));
    }

    [Fact(Timeout = 5000)]
    public async Task Stream_MultipleFilters_EachIndependent()
    {
        // Test: Multiple streams with different filters can coexist
        var userChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var memberChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        
        var userHandle = await _client.StreamAsync("user", userChannel.Writer, 6, CancellationToken.None);
        var memberHandle = await _client.StreamAsync("member-join", memberChannel.Writer, 7, CancellationToken.None);
        
        Assert.Equal(6ul, userHandle.Seq);
        Assert.Equal(7ul, memberHandle.Seq);
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_EventStream_TerminatesStream()
    {
        // Test: Stop command should work for event streams
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var handle = await _client.StreamAsync("user", eventChannel.Writer, 8, CancellationToken.None);
        
        var stopResponse = await _client.StopAsync(handle.Seq, 9, CancellationToken.None);
        
        Assert.Equal("", stopResponse.Error);
    }

    [Fact(Timeout = 10000)]
    public async Task Stream_MonitorAndEventCoexist()
    {
        // Test: Monitor and Stream can both be active simultaneously
        var logChannel = Channel.CreateUnbounded<string>();
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        
        var monitorHandle = await _client.MonitorAsync("debug", logChannel.Writer, 10, CancellationToken.None);
        var streamHandle = await _client.StreamAsync("user", eventChannel.Writer, 11, CancellationToken.None);
        
        Assert.Equal(10ul, monitorHandle.Seq);
        Assert.Equal(11ul, streamHandle.Seq);
        
        // Both should be stoppable independently
        await _client.StopAsync(monitorHandle.Seq, 12, CancellationToken.None);
        await _client.StopAsync(streamHandle.Seq, 13, CancellationToken.None);
    }
}
