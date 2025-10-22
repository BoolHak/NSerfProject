namespace NSerf.Client;

/// <summary>
/// Handler interface for dispatching responses from the background reader.
/// Based on Go's seqHandler interface pattern.
/// </summary>
public interface IResponseHandler
{
    /// <summary>
    /// Handle an incoming response from the server.
    /// </summary>
    Task HandleAsync(ResponseHeader header, MessagePack.MessagePackStreamReader reader);

    /// <summary>
    /// Cleanup resources when handler is deregistered.
    /// Called when stream is stopped or client is disposed.
    /// </summary>
    Task CleanupAsync();
}
