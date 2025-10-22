using MessagePack;
using System.Threading.Channels;

namespace NSerf.Client;

/// <summary>
/// Handler for Monitor command that streams log messages to a channel.
/// Based on Go's monitorHandler pattern.
/// </summary>
public class MonitorHandler : IResponseHandler
{
    private readonly ChannelWriter<string> _logWriter;
    private readonly TaskCompletionSource<bool> _initTcs;
    private bool _initialized;

    public MonitorHandler(ChannelWriter<string> logWriter)
    {
        _logWriter = logWriter;
        _initTcs = new TaskCompletionSource<bool>();
        _initialized = false;
    }

    /// <summary>
    /// Gets a task that completes when the monitor stream is initialized.
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
                _initTcs.SetException(new InvalidOperationException($"Monitor failed: {header.Error}"));
            }
            else
            {
                _initTcs.SetResult(true);
            }
            return;
        }

        // Subsequent responses are log records
        try
        {
            var msgpack = await reader.ReadAsync(CancellationToken.None);
            if (!msgpack.HasValue)
            {
                return;
            }

            var logRecord = MessagePackSerializer.Deserialize<LogRecord>(msgpack.Value);
            
            // Use TryWrite to avoid blocking
            if (!_logWriter.TryWrite(logRecord.Log))
            {
                // Channel full, log dropped
            }
        }
        catch (Exception)
        {
            // Error reading log record, ignore
        }
    }

    public Task CleanupAsync()
    {
        _logWriter.Complete();
        return Task.CompletedTask;
    }
}
