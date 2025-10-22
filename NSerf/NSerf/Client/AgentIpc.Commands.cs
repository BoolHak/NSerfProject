using MessagePack;

namespace NSerf.Client;

/// <summary>
/// Command handlers for AgentIpc server.
/// </summary>
public partial class AgentIpc
{
    private async Task HandleMembersAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        // TODO: Get members from Serf
        var members = Array.Empty<IpcMember>();
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        var body = new MembersResponse { Members = members };
        
        await client.SendAsync(resp, body, cancellationToken);
    }
    
    private async Task HandleJoinAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<JoinRequest>(msgpack!.Value, _serializerOptions);
        
        // TODO: Call Serf.JoinAsync
        var numJoined = 0;
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        var body = new JoinResponse { Num = numJoined };
        
        await client.SendAsync(resp, body, cancellationToken);
    }
    
    private async Task HandleLeaveAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        // TODO: Call Serf.LeaveAsync
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private async Task HandleForceLeaveAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<ForceLeaveRequest>(msgpack!.Value, _serializerOptions);
        
        // TODO: Call Serf.RemoveFailedNodeAsync
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private async Task HandleMembersFilteredAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<MembersFilteredRequest>(msgpack!.Value, _serializerOptions);
        
        // TODO: Get and filter members from Serf
        var members = Array.Empty<IpcMember>();
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        var body = new MembersResponse { Members = members };
        
        await client.SendAsync(resp, body, cancellationToken);
    }
    
    private async Task HandleUserEventAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<EventRequest>(msgpack!.Value, _serializerOptions);
        
        // TODO: Call Serf.UserEvent
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private async Task HandleTagsAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<TagsRequest>(msgpack!.Value, _serializerOptions);
        
        // TODO: Call Serf.SetTags
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private async Task HandleStatsAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        // TODO: Get stats from Serf
        var stats = new Dictionary<string, Dictionary<string, string>>();
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, stats, cancellationToken);
    }
    
    private async Task HandleInstallKeyAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var req = await MessagePackSerializer.DeserializeAsync<KeyRequest>(
            client.GetStream(), _serializerOptions, cancellationToken);
        
        // TODO: Call Serf KeyManager
        var keyResp = new KeyResponse
        {
            Messages = new Dictionary<string, string>(),
            Keys = new Dictionary<string, int>(),
            NumNodes = 0,
            NumErr = 0,
            NumResp = 0
        };
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, keyResp, cancellationToken);
    }
    
    private async Task HandleUseKeyAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var req = await MessagePackSerializer.DeserializeAsync<KeyRequest>(
            client.GetStream(), _serializerOptions, cancellationToken);
        
        // TODO: Call Serf KeyManager
        var keyResp = new KeyResponse
        {
            Messages = new Dictionary<string, string>(),
            Keys = new Dictionary<string, int>(),
            NumNodes = 0,
            NumErr = 0,
            NumResp = 0
        };
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, keyResp, cancellationToken);
    }
    
    private async Task HandleRemoveKeyAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var req = await MessagePackSerializer.DeserializeAsync<KeyRequest>(
            client.GetStream(), _serializerOptions, cancellationToken);
        
        // TODO: Call Serf KeyManager
        var keyResp = new KeyResponse
        {
            Messages = new Dictionary<string, string>(),
            Keys = new Dictionary<string, int>(),
            NumNodes = 0,
            NumErr = 0,
            NumResp = 0
        };
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, keyResp, cancellationToken);
    }
    
    private async Task HandleListKeysAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        // TODO: Call Serf KeyManager
        var keyResp = new KeyResponse
        {
            Messages = new Dictionary<string, string>(),
            Keys = new Dictionary<string, int>(),
            NumNodes = 0,
            NumErr = 0,
            NumResp = 0
        };
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, keyResp, cancellationToken);
    }
    
    private async Task HandleGetCoordinateAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var req = await MessagePackSerializer.DeserializeAsync<CoordinateRequest>(
            client.GetStream(), _serializerOptions, cancellationToken);
        
        // TODO: Call Serf Coordinate
        var coordResp = new CoordinateResponse
        {
            Coord = new Coordinate.Coordinate(),
            Ok = false
        };
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, coordResp, cancellationToken);
    }
}
