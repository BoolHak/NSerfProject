using Microsoft.Extensions.Logging;

namespace NSerf.Client;

/// <summary>
/// Handles per-client log filtering and delivery.
/// Filters logs based on minimum level and sends to IPC client.
/// Phase 16 - Task 2.2 (TDD Implementation).
/// </summary>
internal class LogStream
{
    private readonly object _client;
    private readonly ulong _seq;
    private readonly LogLevel _minLevel;
    private readonly CancellationToken _cancellationToken;

    public LogStream(object client, ulong seq, LogLevel minLevel, CancellationToken cancellationToken)
    {
        _client = client;
        _seq = seq;
        _minLevel = minLevel;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Checks if a log at the given level should be sent.
    /// </summary>
    public bool ShouldSendLog(LogLevel level)
    {
        return level >= _minLevel;
    }

    /// <summary>
    /// Sends a log message to the client.
    /// </summary>
    public async Task SendLogAsync(string message, LogLevel level)
    {
        if (!ShouldSendLog(level)) return;

        var logRecord = new { Log = message };
        var header = new ResponseHeader { Seq = _seq, Error = "" };
        
        if (_client is IpcClientHandler handler)
        {
            await handler.SendAsync(header, logRecord, _cancellationToken);
        }
    }

    /// <summary>
    /// Formats a log message for transmission.
    /// Returns plain string (wire protocol wraps in { "Log": "..." }).
    /// </summary>
    public string FormatLog(string message)
    {
        return message;
    }

    /// <summary>
    /// Stops this log stream.
    /// </summary>
    public void Stop()
    {
        // Stream is stopped via cancellation token
        // No additional cleanup needed
    }
}
