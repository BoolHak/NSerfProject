using MessagePack;

namespace NSerf.Client;

public partial class AgentIpc
{
    /// <summary>
    /// Handles respond command - sends a response to a query.
    /// This allows IPC clients to respond to queries initiated by the Serf cluster.
    /// Note: Full query tracking integration would require tracking active Query objects.
    /// For now, we accept the command to validate IPC protocol.
    /// </summary>
    private async Task HandleRespondAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<RespondRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            // TODO: Full implementation would look up the active Query by req.ID
            // and call Query.RespondAsync(req.Payload)
            // For now, accept the command (query may not exist, but IPC protocol works)
            
            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, null, cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }
}
