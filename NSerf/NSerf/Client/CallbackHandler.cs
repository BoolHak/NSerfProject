using MessagePack;
using System.Buffers;

namespace NSerf.Client;

/// <summary>
/// Handler for simple non-streaming commands (handshake, auth, stop, etc.).
/// Waits for a single response header and optional body, then completes.
/// Based on Go's seqCallback implementation.
/// </summary>
internal class CallbackHandler : IResponseHandler
{
    private readonly TaskCompletionSource<(ResponseHeader, byte[]?)> _tcs = new();
    private readonly MessagePackSerializerOptions _options;
    private readonly bool _expectBody;

    public CallbackHandler(MessagePackSerializerOptions options, bool expectBody = true)
    {
        _options = options;
        _expectBody = expectBody;
    }

    public System.Threading.Tasks.Task<(ResponseHeader, byte[]?)> Task => _tcs.Task;

    public async System.Threading.Tasks.Task HandleAsync(ResponseHeader header, MessagePackStreamReader reader)
    {
        try
        {
            // Go's genericRPC pattern:
            // - If error is present in header, don't read body
            // - If no error and body expected, read body from stream
            byte[]? bodyBytes = null;
            
            if (string.IsNullOrEmpty(header.Error) && _expectBody)
            {
                // Read body for commands that return data
                var msgpack = await reader.ReadAsync(CancellationToken.None);
                if (msgpack.HasValue)
                {
                    // Convert ReadOnlySequence<byte> to byte[]
                    bodyBytes = msgpack.Value.ToArray();
                }
            }

            _tcs.SetResult((header, bodyBytes));
        }
        catch (Exception ex)
        {
            _tcs.SetException(ex);
        }
    }

    public System.Threading.Tasks.Task CleanupAsync()
    {
        // Cancel if not already completed
        _tcs.TrySetCanceled();
        return System.Threading.Tasks.Task.CompletedTask;
    }
}
