using MessagePack;
using System.Threading.Channels;

namespace NSerf.Client;

/// <summary>
/// Handler for Stream command that streams events to a channel.
/// Based on Go's streamHandler pattern.
/// </summary>
public class StreamHandler : IResponseHandler
{
    private readonly ChannelWriter<Dictionary<string, object>> _eventWriter;
    private readonly TaskCompletionSource<bool> _initTcs;
    private bool _initialized;

    public StreamHandler(ChannelWriter<Dictionary<string, object>> eventWriter)
    {
        _eventWriter = eventWriter;
        _initTcs = new TaskCompletionSource<bool>();
        _initialized = false;
    }

    /// <summary>
    /// Gets a task that completes when the event stream is initialized.
    /// </summary>
    public Task<bool> InitTask => _initTcs.Task;

    public async Task HandleAsync(ResponseHeader header, MessagePackStreamReader reader)
    {
        // First response is the initialization response
        if (!_initialized)
        {
            _initialized = true;
            
            if (!string.IsNullOrEmpty(header.Error))
            {
                _initTcs.SetException(new InvalidOperationException($"Stream failed: {header.Error}"));
            }
            else
            {
                _initTcs.SetResult(true);
            }
            return;
        }

        // Subsequent responses are event records
        try
        {
            var msgpack = await reader.ReadAsync(CancellationToken.None);
            if (!msgpack.HasValue)
            {
                return;
            }

            var eventData = MessagePackSerializer.Deserialize<Dictionary<string, object>>(msgpack.Value);
            
            // Use TryWrite to avoid blocking
            if (!_eventWriter.TryWrite(eventData))
            {
                // Channel full, event dropped
            }
        }
        catch (Exception)
        {
            // Error reading event, ignore
        }
    }

    public Task CleanupAsync()
    {
        _eventWriter.Complete();
        return Task.CompletedTask;
    }
}
