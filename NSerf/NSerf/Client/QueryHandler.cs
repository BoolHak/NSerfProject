using MessagePack;
using System.Threading.Channels;

namespace NSerf.Client;

/// <summary>
/// Handler for query command - manages bidirectional query/response streaming.
/// Processes ack, response, and done records, sending them to appropriate channels.
/// Based on Go's queryHandler implementation.
/// </summary>
internal class QueryHandler : IResponseHandler
{
    private readonly MessagePackSerializerOptions _options;
    private readonly ChannelWriter<string>? _ackWriter;
    private readonly ChannelWriter<NodeResponse>? _respWriter;
    private readonly Action<ulong> _deregisterCallback;
    private readonly ulong _seq;
    private readonly TaskCompletionSource<string> _initTcs = new();
    private bool _initialized;
    private bool _closed;

    public QueryHandler(
        MessagePackSerializerOptions options,
        ulong seq,
        ChannelWriter<string>? ackWriter,
        ChannelWriter<NodeResponse>? respWriter,
        Action<ulong> deregisterCallback)
    {
        _options = options;
        _seq = seq;
        _ackWriter = ackWriter;
        _respWriter = respWriter;
        _deregisterCallback = deregisterCallback;
    }

    public Task<string> InitTask => _initTcs.Task;

    public async Task HandleAsync(ResponseHeader header, MessagePackStreamReader reader)
    {
        try
        {
            // First response is initialization confirmation
            if (!_initialized)
            {
                _initialized = true;
                
                // If error in header, fail initialization
                if (!string.IsNullOrEmpty(header.Error))
                {
                    _initTcs.SetResult(header.Error);
                    return;
                }
                
                // Success - no error
                _initTcs.SetResult("");
                return;
            }

            // Subsequent responses are query records (ack/response/done)
            var msgpack = await reader.ReadAsync(CancellationToken.None);
            if (!msgpack.HasValue)
            {
                _deregisterCallback(_seq);
                return;
            }

            var record = MessagePackSerializer.Deserialize<QueryRecord>(msgpack.Value, _options);

            switch (record.Type)
            {
                case "ack":
                    // Send acknowledgement to ack channel (non-blocking)
                    if (_ackWriter != null)
                    {
                        if (!_ackWriter.TryWrite(record.From))
                        {
                            // Channel full - log but don't block
                            // In production, should have proper logging
                        }
                    }
                    break;

                case "response":
                    // Send response to response channel (non-blocking)
                    if (_respWriter != null)
                    {
                        var nodeResponse = new NodeResponse
                        {
                            From = record.From,
                            Payload = record.Payload
                        };
                        
                        if (!_respWriter.TryWrite(nodeResponse))
                        {
                            // Channel full - log but don't block
                        }
                    }
                    break;

                case "done":
                    // Query complete - deregister handler
                    _deregisterCallback(_seq);
                    break;

                default:
                    // Unknown record type - deregister to prevent hanging
                    _deregisterCallback(_seq);
                    break;
            }
        }
        catch (Exception)
        {
            // On any error, deregister to prevent hanging
            _deregisterCallback(_seq);
            throw;
        }
    }

    public async Task CleanupAsync()
    {
        if (_closed)
            return;

        _closed = true;

        // Close channels
        _ackWriter?.Complete();
        _respWriter?.Complete();

        // If not initialized, signal error
        if (!_initialized)
        {
            _initTcs.TrySetResult("Stream closed");
        }

        await Task.CompletedTask;
    }
}
