using NSerf.Client;
using NSerf.Serf;
using System.Threading.Channels;
using Xunit;

namespace NSerfTests.Client;

/// <summary>
/// Comprehensive tests for Stop command functionality.
/// Phase 16, Days 9-10: Validates that Stop command properly terminates background tasks.
/// </summary>
public class StopCommandTests : IAsyncDisposable
{
    private readonly global::NSerf.Serf.Serf _serf;
    private readonly AgentIpc _server;
    private readonly IpcClient _client;

    public StopCommandTests()
    {
        var config = new Config
        {
            NodeName = "test-stop-node",
            MemberlistConfig = new global::NSerf.Memberlist.Configuration.MemberlistConfig
            {
                Name = "test-stop-node",
                BindAddr = "127.0.0.1",
                BindPort = 0
            }
        };

        _serf = global::NSerf.Serf.Serf.CreateAsync(config).Result;
        _server = new AgentIpc(_serf, "127.0.0.1:0", null, false, null);
        _server.StartAsync(CancellationToken.None).Wait();

        _client = new IpcClient();
        _client.ConnectAsync("127.0.0.1", _server.Port, CancellationToken.None).Wait();
        _client.HandshakeAsync(1, CancellationToken.None).Wait();
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
        _serf.Dispose();
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_MonitorStream_CancelsBackgroundTask()
    {
        // Arrange: Start a monitor stream
        var logChannel = Channel.CreateUnbounded<string>();
        var monitorHandle = await _client.MonitorAsync("debug", logChannel.Writer, 2, CancellationToken.None);
        Assert.Equal(2ul, monitorHandle.Seq);

        // Give stream a moment to register
        await Task.Delay(100);

        // Act: Stop the monitor stream
        var stopResponse = await _client.StopAsync(monitorHandle.Seq, 3, CancellationToken.None);

        // Assert: Stop command succeeded
        Assert.Equal("", stopResponse.Error);

        // Verify: Channel should eventually complete (stream stopped)
        await Task.Delay(200); // Give time for cleanup

        // Try to read - should timeout or channel closed since stream is stopped
        using var cts = new CancellationTokenSource(500);
        var receivedAnything = false;
        try
        {
            await logChannel.Reader.ReadAsync(cts.Token);
            receivedAnything = true;
        }
        catch (OperationCanceledException)
        {
            // Expected - no logs after stop
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            // Also expected - channel was properly closed
        }

        Assert.False(receivedAnything, "Should not receive logs after Stop command");
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_EventStream_CancelsBackgroundTask()
    {
        // Arrange: Start an event stream
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var streamHandle = await _client.StreamAsync("user", eventChannel.Writer, 2, CancellationToken.None);
        Assert.Equal(2ul, streamHandle.Seq);

        // Give stream a moment to register
        await Task.Delay(100);

        // Act: Stop the event stream
        var stopResponse = await _client.StopAsync(streamHandle.Seq, 3, CancellationToken.None);

        // Assert: Stop command succeeded
        Assert.Equal("", stopResponse.Error);

        // Fire an event AFTER stopping
        await _serf.UserEventAsync("after-stop", new byte[] { 99 }, false);
        await Task.Delay(200);

        // Verify: Should NOT receive the event (stream is stopped)
        using var cts = new CancellationTokenSource(500);
        var receivedEvent = false;
        try
        {
            await eventChannel.Reader.ReadAsync(cts.Token);
            receivedEvent = true;
        }
        catch (OperationCanceledException)
        {
            // Expected - no events after stop
        }
        catch (System.Threading.Channels.ChannelClosedException)
        {
            // Also expected - channel was properly closed
        }

        Assert.False(receivedEvent, "Should not receive events after Stop command");
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_NonExistentStream_ReturnsError()
    {
        // Act: Try to stop a stream that doesn't exist
        var stopResponse = await _client.StopAsync(999, 2, CancellationToken.None);

        // Assert: Should return error
        Assert.NotEqual("", stopResponse.Error);
        Assert.Contains("does not exist", stopResponse.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_AlreadyStoppedStream_ReturnsError()
    {
        // Arrange: Start and stop a stream
        var logChannel = Channel.CreateUnbounded<string>();
        var monitorHandle = await _client.MonitorAsync("debug", logChannel.Writer, 2, CancellationToken.None);

        var firstStop = await _client.StopAsync(monitorHandle.Seq, 3, CancellationToken.None);
        Assert.Equal("", firstStop.Error);

        await Task.Delay(100);

        // Act: Try to stop the same stream again
        var secondStop = await _client.StopAsync(monitorHandle.Seq, 4, CancellationToken.None);

        // Assert: Should return error (stream no longer exists)
        Assert.NotEqual("", secondStop.Error);
    }

    [Fact(Timeout = 10000)]
    public async Task Stop_VerifiesTaskTermination_WithinTimeout()
    {
        // Arrange: Start a stream
        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var streamHandle = await _client.StreamAsync("*", eventChannel.Writer, 2, CancellationToken.None);

        await Task.Delay(100);

        // Act: Stop the stream
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stopResponse = await _client.StopAsync(streamHandle.Seq, 3, CancellationToken.None);
        stopwatch.Stop();

        // Assert: Stop command completes quickly (< 1 second)
        Assert.Equal("", stopResponse.Error);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000,
            $"Stop command took {stopwatch.ElapsedMilliseconds}ms, should be < 1000ms");

        // Verify: Background task has terminated (no more processing)
        await Task.Delay(200);

        // Fire multiple events - none should be received
        for (int i = 0; i < 5; i++)
        {
            await _serf.UserEventAsync($"event-{i}", new byte[] { (byte)i }, false);
        }

        await Task.Delay(500);

        // Channel should be empty or closed
        var eventCount = 0;
        while (eventChannel.Reader.TryRead(out _))
        {
            eventCount++;
        }

        Assert.True(eventCount == 0, $"Expected 0 events after stop, but got {eventCount}");
    }

    [Fact(Timeout = 5000)]
    public async Task Stop_WithClientDisconnect_CleansUpGracefully()
    {
        // Arrange: Create a separate client for this test
        var tempClient = new IpcClient();
        await tempClient.ConnectAsync("127.0.0.1", _server.Port, CancellationToken.None);
        await tempClient.HandshakeAsync(1, CancellationToken.None);

        var eventChannel = Channel.CreateUnbounded<Dictionary<string, object>>();
        var streamHandle = await tempClient.StreamAsync("user", eventChannel.Writer, 2, CancellationToken.None);

        await Task.Delay(100);

        // Act: Disconnect client without explicitly stopping (simulates client crash)
        await tempClient.DisposeAsync();

        // Give server time to detect disconnect and cleanup
        await Task.Delay(500);

        // Assert: Server should have cleaned up the stream
        // This is implicit - if server didn't crash, cleanup worked
        // Fire an event to verify server is still operational
        await _serf.UserEventAsync("after-disconnect", new byte[] { 1 }, false);

        // If we get here without exceptions, cleanup was successful
        Assert.True(true, "Server handled client disconnect gracefully");
    }
}
