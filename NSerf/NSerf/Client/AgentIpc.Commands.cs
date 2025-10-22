using MessagePack;
using System.Net;
using System.Text.RegularExpressions;
using NSerf.Serf;

namespace NSerf.Client;

/// <summary>
/// Command handlers for AgentIpc server.
/// </summary>
public partial class AgentIpc
{
    private async Task HandleMembersAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var serfMembers = _serf.Members();
        var members = serfMembers.Select(ConvertToIpcMember).ToArray();

        var resp = new ResponseHeader { Seq = seq, Error = "" };
        var body = new MembersResponse { Members = members };

        await client.SendAsync(resp, body, cancellationToken);
    }

    private async Task HandleJoinAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<JoinRequest>(msgpack!.Value, _serializerOptions, cancellationToken);

        try
        {
            var numJoined = await _serf.JoinAsync(req.Existing, req.Replay);

            var resp = new ResponseHeader { Seq = seq, Error = "" };
            var body = new JoinResponse { Num = numJoined };

            await client.SendAsync(resp, body, cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleLeaveAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        try
        {
            await _serf.LeaveAsync();
            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, null, cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleForceLeaveAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<ForceLeaveRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            await _serf.RemoveFailedNodeAsync(req.Node, req.Prune);
            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, null, cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleMembersFilteredAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<MembersFilteredRequest>(msgpack!.Value, _serializerOptions);

        var serfMembers = _serf.Members();
        var filteredMembers = FilterMembers(serfMembers, req.Tags, req.Status, req.Name);
        var members = filteredMembers.Select(ConvertToIpcMember).ToArray();

        var resp = new ResponseHeader { Seq = seq, Error = "" };
        var body = new MembersResponse { Members = members };

        await client.SendAsync(resp, body, cancellationToken);
    }

    private async Task HandleUserEventAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<EventRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            await _serf.UserEventAsync(req.Name, req.Payload, req.Coalesce);
            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, null, cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleTagsAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<TagsRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            // Merge tags
            var currentTags = new Dictionary<string, string>(_serf.Config.Tags);
            if (req.Tags != null)
            {
                foreach (var kvp in req.Tags)
                {
                    currentTags[kvp.Key] = kvp.Value;
                }
            }

            // Delete tags
            if (req.DeleteTags != null)
            {
                foreach (var key in req.DeleteTags)
                {
                    currentTags.Remove(key);
                }
            }

            await _serf.SetTagsAsync(currentTags);
            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, null, cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleStatsAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var stats = new Dictionary<string, Dictionary<string, string>>();

        // Agent stats
        var agentStats = new Dictionary<string, string>
        {
            ["name"] = _serf.Config.NodeName
        };
        stats["agent"] = agentStats;

        // Get memberlist stats if available
        if (_serf.Memberlist != null)
        {
            var statsDict = new Dictionary<string, string>
            {
                // Basic memberlist stats from available methods
                ["num_nodes"] = _serf.Memberlist.NumMembers().ToString(),
                ["msg_alive"] = "0",  // Placeholder - would need stats tracking
                ["msg_dead"] = "0",
                ["msg_suspect"] = "0"
            };

            stats["memberlist"] = statsDict;
        }

        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, stats, cancellationToken);
    }

    private async Task HandleInstallKeyAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<KeyRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            var keyManager = new KeyManager(_serf);
            var keyResp = await keyManager.InstallKey(req.Key);

            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, ConvertKeyResponse(keyResp), cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleUseKeyAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<KeyRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            var keyManager = new KeyManager(_serf);
            var keyResp = await keyManager.UseKey(req.Key);

            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, ConvertKeyResponse(keyResp), cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleRemoveKeyAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<KeyRequest>(msgpack!.Value, _serializerOptions);

        try
        {
            var keyManager = new KeyManager(_serf);
            var keyResp = await keyManager.RemoveKey(req.Key);

            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, ConvertKeyResponse(keyResp), cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleListKeysAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        try
        {
            var keyManager = new KeyManager(_serf);
            var keyResp = await keyManager.ListKeys();

            var resp = new ResponseHeader { Seq = seq, Error = "" };
            await client.SendAsync(resp, ConvertKeyResponse(keyResp), cancellationToken);
        }
        catch (Exception ex)
        {
            var resp = new ResponseHeader { Seq = seq, Error = ex.Message };
            await client.SendAsync(resp, null, cancellationToken);
        }
    }

    private async Task HandleGetCoordinateAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<CoordinateRequest>(msgpack!.Value, _serializerOptions);

        // TODO: Call Serf Coordinate
        var coordResp = new CoordinateResponse
        {
            Coord = new Coordinate.Coordinate(),
            Ok = false
        };

        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, coordResp, cancellationToken);
    }

    private async Task HandleMonitorAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<MonitorRequest>(msgpack!.Value, _serializerOptions);

        // Check for duplicate monitor
        if (client.HasMonitor(seq))
        {
            var errorResp = new ResponseHeader { Seq = seq, Error = IpcProtocol.MonitorExists };
            await client.SendAsync(errorResp, null, cancellationToken);
            return;
        }

        // Register monitor
        if (!client.RegisterMonitor(seq))
        {
            var errorResp = new ResponseHeader { Seq = seq, Error = IpcProtocol.MonitorExists };
            await client.SendAsync(errorResp, null, cancellationToken);
            return;
        }

        // Send initial success response
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);

        // TODO: Start streaming logs to client based on req.LogLevel
        // For now, just acknowledge the subscription
    }

    private async Task HandleStreamAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<StreamRequest>(msgpack!.Value, _serializerOptions);

        // Check for duplicate stream
        if (client.HasStream(seq))
        {
            var errorResp = new ResponseHeader { Seq = seq, Error = IpcProtocol.StreamExists };
            await client.SendAsync(errorResp, null, cancellationToken);
            return;
        }

        // Register event stream
        if (!client.RegisterStream(seq))
        {
            var errorResp = new ResponseHeader { Seq = seq, Error = IpcProtocol.StreamExists };
            await client.SendAsync(errorResp, null, cancellationToken);
            return;
        }

        // Send initial success response
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);

        // TODO: Start streaming events to client based on req.Type filter
        // For now, just acknowledge the subscription
    }

    private async Task HandleStopAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<StopRequest>(msgpack!.Value, _serializerOptions);

        // Check if stream exists
        if (!client.HasStream(req.Stop))
        {
            var errorResp = new ResponseHeader { Seq = seq, Error = "Stream with given sequence does not exist" };
            await client.SendAsync(errorResp, null, cancellationToken);
            return;
        }

        // Unregister the stream
        client.UnregisterStream(req.Stop);

        // TODO: Actually stop the streaming task
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        await client.SendAsync(resp, null, cancellationToken);
    }

    /// <summary>
    /// Converts a Serf Member to an IpcMember for wire protocol.
    /// </summary>
    private IpcMember ConvertToIpcMember(Serf.Member member)
    {
        return new IpcMember
        {
            Name = member.Name,
            Addr = member.Addr.GetAddressBytes(),
            Port = member.Port,
            Tags = new Dictionary<string, string>(member.Tags),
            Status = member.Status.ToStatusString(),
            ProtocolMin = member.ProtocolMin,
            ProtocolMax = member.ProtocolMax,
            ProtocolCur = member.ProtocolCur,
            DelegateMin = member.DelegateMin,
            DelegateMax = member.DelegateMax,
            DelegateCur = member.DelegateCur
        };
    }

    /// <summary>
    /// Filters members by tags, status, and name using regex.
    /// Go serf anchors regex with ^ and $, we do the same.
    /// </summary>
    private IEnumerable<Serf.Member> FilterMembers(
        Serf.Member[] members,
        Dictionary<string, string>? tagFilters,
        string? statusFilter,
        string? nameFilter)
    {
        var filtered = members.AsEnumerable();

        // Filter by tags (regex)
        if (tagFilters != null && tagFilters.Count > 0)
        {
            filtered = filtered.Where(m =>
            {
                foreach (var filter in tagFilters)
                {
                    if (!m.Tags.TryGetValue(filter.Key, out var value))
                        return false;

                    var regex = new Regex($"^{filter.Value}$", RegexOptions.Compiled);
                    if (!regex.IsMatch(value))
                        return false;
                }
                return true;
            });
        }

        // Filter by status (regex)
        if (!string.IsNullOrEmpty(statusFilter))
        {
            var statusRegex = new Regex($"^{statusFilter}$", RegexOptions.Compiled);
            filtered = filtered.Where(m => statusRegex.IsMatch(m.Status.ToStatusString()));
        }

        // Filter by name (regex)
        if (!string.IsNullOrEmpty(nameFilter))
        {
            var nameRegex = new Regex($"^{nameFilter}$", RegexOptions.Compiled);
            filtered = filtered.Where(m => nameRegex.IsMatch(m.Name));
        }

        return filtered;
    }

    /// <summary>
    /// Converts Serf KeyResponse to IPC KeyResponse.
    /// </summary>
    private Client.KeyResponse ConvertKeyResponse(Serf.KeyResponse serfResponse)
    {
        return new Client.KeyResponse
        {
            Messages = serfResponse.Messages != null
                ? new Dictionary<string, string>(serfResponse.Messages)
                : new Dictionary<string, string>(),
            Keys = serfResponse.Keys != null
                ? new Dictionary<string, int>(serfResponse.Keys)
                : new Dictionary<string, int>(),
            NumNodes = serfResponse.NumNodes,
            NumErr = serfResponse.NumErr,
            NumResp = serfResponse.NumResp
        };
    }
}
