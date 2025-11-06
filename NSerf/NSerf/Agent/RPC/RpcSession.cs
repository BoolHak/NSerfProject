// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using System.Runtime.InteropServices;
using MessagePack;
using NSerf.Client;
using NSerf.Serf;

namespace NSerf.Agent.RPC;

public class RpcSession : IAsyncDisposable
{
    private readonly SerfAgent _agent;
    private readonly TcpClient _client;
    private readonly string? _authKey;
    private readonly NetworkStream? _stream;
    private readonly MessagePackStreamReader? _reader;
    private bool _authenticated;
    private bool _disposed;
    private int _clientVersion;  // 0 = no handshake yet
    private readonly SemaphoreSlim _writeLock = new(1, 1);  // CRITICAL: Prevent overlapping writes

    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.None);

    public RpcSession(SerfAgent agent, TcpClient client, string? authKey)
    {
        _agent = agent;
        _client = client;
        _authKey = authKey;
        _stream = client.GetStream();
        _reader = new MessagePackStreamReader(_stream, leaveOpen: true);
    }

    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed && _reader != null)
            {
                RequestHeader? header;
                try
                {
                    var headerBytes = await _reader.ReadAsync(cancellationToken);
                    if (!headerBytes.HasValue)
                    {
                        break;
                    }

                    header = MessagePackSerializer.Deserialize<RequestHeader>(headerBytes.Value, MsgPackOptions, cancellationToken);
                }
                catch (IOException ex)
                {
                    // Windows throws WSA errors on EOF - don't log as errors
                    if (!IsWindowsSocketClosed(ex))
                    {
                        // Actual IO error, not normal disconnect
                    }
                    break;
                }
                catch (SocketException)
                {
                    // Normal disconnect
                    break;
                }
                catch (MessagePackSerializationException)
                {
                    // Malformed message
                    break;
                }
                catch (Exception)
                {
                    break;
                }

                await HandleCommandAsync(header, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Connection closed
        }
        catch (Exception)
        {
            // Session error
        }
    }

    private async Task HandleCommandAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        try
        {
            // Ensure a handshake is performed before other commands
            if (header.Command != RpcCommands.Handshake && _clientVersion == 0)
            {
                await SendErrorAsync(header.Seq, "Handshake required", cancellationToken);
                return;
            }

            // Ensure a client has authenticated after handshake if necessary
            if (!string.IsNullOrEmpty(_authKey) && !_authenticated &&
                header.Command != RpcCommands.Auth && header.Command != RpcCommands.Handshake)
            {
                await SendErrorAsync(header.Seq, "Authentication required", cancellationToken);
                return;
            }

            switch (header.Command)
            {
                case RpcCommands.Handshake:
                    await HandleHandshakeAsync(header, cancellationToken);
                    break;

                case RpcCommands.Auth:
                    await HandleAuthAsync(header, cancellationToken);
                    break;

                case RpcCommands.Members:
                    await HandleMembersAsync(header, cancellationToken);
                    break;

                case RpcCommands.Join:
                    await HandleJoinAsync(header, cancellationToken);
                    break;

                case RpcCommands.Leave:
                    await HandleLeaveAsync(header, cancellationToken);
                    break;

                case RpcCommands.MembersFiltered:
                    await HandleMembersFilteredAsync(header, cancellationToken);
                    break;

                case RpcCommands.ForceLeave:
                    await HandleForceLeaveAsync(header, cancellationToken);
                    break;

                case RpcCommands.Event:
                    await HandleUserEventAsync(header, cancellationToken);
                    break;

                case RpcCommands.Tags:
                    await HandleTagsAsync(header, cancellationToken);
                    break;

                case RpcCommands.Query:
                    await HandleQueryAsync(header, cancellationToken);
                    break;

                case RpcCommands.Stats:
                    await HandleStatsAsync(header, cancellationToken);
                    break;

                case RpcCommands.GetCoordinate:
                    await HandleGetCoordinateAsync(header, cancellationToken);
                    break;

                case RpcCommands.Monitor:
                    await HandleMonitorAsync(header, cancellationToken);
                    break;

                case RpcCommands.Stream:
                    await HandleStreamAsync(header, cancellationToken);
                    break;

                default:
                    await SendErrorAsync(header.Seq, $"Unknown command: {header.Command}", cancellationToken);
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendErrorAsync(header.Seq, ex.Message, cancellationToken);
        }
    }

    private async Task HandleHandshakeAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<HandshakeRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        // Check for duplicate handshake
        if (_clientVersion != 0)
        {
            await SendErrorAsync(header.Seq, "Duplicate handshake", cancellationToken);
            return;
        }

        var response = new ResponseHeader
        {
            Seq = header.Seq,
            Error = request.Version > RpcConstants.MaxIpcVersion
                ? $"Unsupported version: {request.Version}"
                : string.Empty
        };

        // Store client version if handshake successful
        if (string.IsNullOrEmpty(response.Error))
        {
            _clientVersion = request.Version;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleAuthAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<AuthRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        var error = string.Empty;
        if (!string.IsNullOrEmpty(_authKey) && request.AuthKey != _authKey)
        {
            error = "Invalid auth key";
        }
        else
        {
            _authenticated = true;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = error };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleMembersAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var members = _agent.Serf?.Members() ?? [];

        var rpcMembers = members.Select(m => new Client.Responses.Member
        {
            Name = m.Name,
            Addr = m.Addr.GetAddressBytes(),
            Port = m.Port,
            Tags = m.Tags,
            Status = m.Status.ToString().ToLowerInvariant(),
            ProtocolMin = m.ProtocolMin,
            ProtocolMax = m.ProtocolMax,
            ProtocolCur = m.ProtocolCur,
            DelegateMin = m.DelegateMin,
            DelegateMax = m.DelegateMax,
            DelegateCur = m.DelegateCur
        }).ToArray();

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);

            var membersResponse = new Client.Responses.MembersResponse { Members = rpcMembers };
            var bodyBytes = MessagePackSerializer.Serialize(membersResponse, MsgPackOptions, cancellationToken);
            await _stream.WriteAsync(bodyBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleJoinAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<Client.Requests.JoinRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        var joined = await _agent.Serf!.JoinAsync(request.Existing, request.Replay);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);

            var joinResponse = new Client.Responses.JoinResponse { Num = joined };
            var bodyBytes = MessagePackSerializer.Serialize(joinResponse, MsgPackOptions, cancellationToken);
            await _stream.WriteAsync(bodyBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleLeaveAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        await _agent.Serf!.LeaveAsync();

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleMembersFilteredAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<Client.Requests.MembersFilteredRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        var allMembers = _agent.Serf?.Members() ?? [];

        // Pre-compile regex patterns with anchors (^$) for an exact match
        System.Text.RegularExpressions.Regex? nameRegex = null;
        System.Text.RegularExpressions.Regex? statusRegex = null;
        Dictionary<string, System.Text.RegularExpressions.Regex>? tagRegexes = null;

        try
        {
            if (!string.IsNullOrEmpty(request.Name))
                nameRegex = new System.Text.RegularExpressions.Regex($"^{request.Name}$");

            if (!string.IsNullOrEmpty(request.Status))
                statusRegex = new System.Text.RegularExpressions.Regex($"^{request.Status}$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (request.Tags.Count > 0)
            {
                tagRegexes = [];
                foreach (var tag in request.Tags)
                {
                    tagRegexes[tag.Key] = new System.Text.RegularExpressions.Regex($"^{tag.Value}$");
                }
            }
        }
        catch (ArgumentException ex)
        {
            await SendErrorAsync(header.Seq, $"Invalid regex pattern: {ex.Message}", cancellationToken);
            return;
        }

        var filtered = allMembers.Where(m =>
        {
            if (statusRegex != null && !statusRegex.IsMatch(m.Status.ToString().ToLowerInvariant()))
                return false;

            if (nameRegex != null && !nameRegex.IsMatch(m.Name))
                return false;

            if (tagRegexes == null) return true;

            foreach (var tagRegex in tagRegexes)
            {
                if (!m.Tags.TryGetValue(tagRegex.Key, out var value) || !tagRegex.Value.IsMatch(value))
                    return false;
            }
            return true;
        }).ToArray();

        var rpcMembers = filtered.Select(m => new Client.Responses.Member
        {
            Name = m.Name,
            Addr = m.Addr.GetAddressBytes(),
            Port = m.Port,
            Tags = m.Tags,
            Status = m.Status.ToString().ToLowerInvariant(),
            ProtocolMin = m.ProtocolMin,
            ProtocolMax = m.ProtocolMax,
            ProtocolCur = m.ProtocolCur,
            DelegateMin = m.DelegateMin,
            DelegateMax = m.DelegateMax,
            DelegateCur = m.DelegateCur
        }).ToArray();

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);

            var membersResponse = new Client.Responses.MembersResponse { Members = rpcMembers };
            var bodyBytes = MessagePackSerializer.Serialize(membersResponse, MsgPackOptions, cancellationToken);
            await _stream.WriteAsync(bodyBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleForceLeaveAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<Client.Requests.ForceLeaveRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        await _agent.Serf!.RemoveFailedNodeAsync(request.Node);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleUserEventAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<Client.Requests.EventRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        await _agent.Serf!.UserEventAsync(request.Name, request.Payload, request.Coalesce);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleTagsAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<Client.Requests.TagsRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        // Merge existing tags with new tags, excluding deleted tags
        var mergedTags = new Dictionary<string, string>();

        // Start with existing tags
        var currentTags = _agent.Serf?.Config.Tags ?? [];

        foreach (var tag in currentTags
                     .Where(tag => !request.DeleteTags.Contains(tag.Key)))
        {
            mergedTags[tag.Key] = tag.Value;
        }

        // Add/update with new tags
        foreach (var tag in request.Tags)
        {
            mergedTags[tag.Key] = tag.Value;
        }

        // Apply the merged tags
        await _agent.SetTagsAsync(mergedTags);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleQueryAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue) return;

        var request = MessagePackSerializer.Deserialize<Client.Requests.QueryRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        // Parse filter tags
        Dictionary<string, string>? filterTags = null;
        if (!string.IsNullOrEmpty(request.FilterTags))
        {
            try
            {
                filterTags = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(request.FilterTags);
            }
            catch
            {
                // If parsing fails, leave as null
            }
        }

        var queryParam = new QueryParam
        {
            FilterNodes = string.IsNullOrEmpty(request.FilterNodes) ? null : [request.FilterNodes],
            FilterTags = filterTags,
            RequestAck = request.RequestAck,
            Timeout = TimeSpan.FromSeconds(request.Timeout)
        };

        // Start the query
        QueryResponse? queryResp = null;
        string? errorMsg = null;
        try
        {
            queryResp = await _agent.Serf!.QueryAsync(request.Name, request.Payload, queryParam);
        }
        catch (Exception ex)
        {
            errorMsg = ex.Message;
        }

        // Send initial response with query ID
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = errorMsg ?? string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);

            // Send query ID in the response body (Go returns nil on error)
            if (queryResp != null)
            {
                var queryResponse = new Client.Responses.QueryResponse { Id = queryResp.Id };
                var bodyBytes = MessagePackSerializer.Serialize(queryResponse, MsgPackOptions, cancellationToken);
                await _stream.WriteAsync(bodyBytes, cancellationToken);
            }
            else
            {
                // Send empty body on error
                var emptyBody = MessagePackSerializer.Serialize(new { }, MsgPackOptions, cancellationToken);
                await _stream.WriteAsync(emptyBody, cancellationToken);
            }
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        // Stream the query responses asynchronously (Go: defer go qs.Stream)
        if (queryResp != null)
        {
            var streamer = new QueryResponseStream(_writeLock, _stream!, header.Seq, queryResp);
            _ = Task.Run(() => streamer.StreamAsync(cancellationToken), cancellationToken);
        }
    }

    private async Task HandleStatsAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var stats = new Dictionary<string, Dictionary<string, string>>
        {
            ["agent"] = new()
            {
                ["name"] = _agent.Serf?.Config.NodeName ?? "unknown"
            },
            ["serf"] = new()
            {
                ["members"] = _agent.Serf?.NumMembers().ToString() ?? "0",
                ["event_time"] = _agent.Serf?.EventClock.Time().ToString() ?? "0",
                ["query_time"] = _agent.Serf?.QueryClock.Time().ToString() ?? "0"
            },
            ["runtime"] = new()
            {
                ["os"] = Environment.OSVersion.Platform.ToString(),
                ["arch"] = RuntimeInformation.ProcessArchitecture.ToString()
            }
        };

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);

            var statsResponse = new Client.Responses.StatsResponse { Stats = stats };
            var bodyBytes = MessagePackSerializer.Serialize(statsResponse, MsgPackOptions, cancellationToken);
            await _stream.WriteAsync(bodyBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleGetCoordinateAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue)
        {
            await SendErrorAsync(header.Seq, "Failed to read coordinate request", cancellationToken);
            return;
        }

        Client.Requests.CoordinateRequest? request;
        try
        {
            request = MessagePackSerializer.Deserialize<Client.Requests.CoordinateRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            await SendErrorAsync(header.Seq, $"Failed to decode coordinate request: {ex.Message}", cancellationToken);
            return;
        }

        if (string.IsNullOrEmpty(request.Node))
        {
            await SendErrorAsync(header.Seq, "Node name is required", cancellationToken);
            return;
        }

        var coord = _agent.Serf?.GetCachedCoordinate(request.Node);

        var response = new Client.Responses.CoordinateResponse
        {
            Ok = coord != null,
            Coord = coord != null ? new Client.Responses.Coordinate
            {
                Vec = [.. coord.Vec.Select(v => (float)v)],
                Error = (float)coord.Error,
                Adjustment = (float)coord.Adjustment,
                Height = (float)coord.Height
            } : null
        };

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var headerResponse = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(headerResponse, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);

            var bodyBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream.WriteAsync(bodyBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task HandleMonitorAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        // Read monitor request
        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue)
        {
            await SendErrorAsync(header.Seq, "Failed to read monitor request", cancellationToken);
            return;
        }

        var request = MessagePackSerializer.Deserialize<Client.Requests.MonitorRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        // Send success response
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        // Register log handler and stream logs
        // Apply log-level filtering as requested by the client
        var requestedLevel = LogLevelExtensions.FromString(request.LogLevel ?? "INFO");
        var baseHandler = new RpcLogHandler(_stream!, _writeLock, cancellationToken);
        var filteredHandler = new FilteredLogHandler(baseHandler, requestedLevel);
        _agent.LogWriter?.RegisterHandler(filteredHandler);

        try
        {
            // Keep streaming until the client disconnects or cancellation
            await Task.Delay(-1, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        finally
        {
            _agent.LogWriter?.DeregisterHandler(filteredHandler);
            baseHandler.Dispose();
        }
    }

    private async Task HandleStreamAsync(RequestHeader header, CancellationToken cancellationToken)
    {
        if (!CheckAuth())
        {
            await SendErrorAsync(header.Seq, "Not authenticated", cancellationToken);
            return;
        }

        // Read stream request
        var requestBytes = await _reader!.ReadAsync(cancellationToken);
        if (!requestBytes.HasValue)
        {
            await SendErrorAsync(header.Seq, "Failed to read stream request", cancellationToken);
            return;
        }

        var request = MessagePackSerializer.Deserialize<Client.Requests.StreamRequest>(requestBytes.Value, MsgPackOptions, cancellationToken);

        // Send success response
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = header.Seq, Error = string.Empty };
            var responseBytes = MessagePackSerializer.Serialize(response, MsgPackOptions, cancellationToken);
            await _stream!.WriteAsync(responseBytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        // Register event handler and stream events
        var eventHandler = new RpcEventHandler(_stream!, _writeLock, request.Type, cancellationToken);
        _agent.RegisterEventHandler(eventHandler);

        try
        {
            // Keep streaming until the client disconnects or cancellation
            await Task.Delay(-1, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        finally
        {
            _agent.DeregisterEventHandler(eventHandler);
            eventHandler.Dispose();
        }
    }

    private async Task SendErrorAsync(ulong seq, string error, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var response = new ResponseHeader { Seq = seq, Error = error };
            await MessagePackSerializer.SerializeAsync(_stream!, response, MsgPackOptions, cancellationToken);
            await _stream!.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private bool CheckAuth()
    {
        return string.IsNullOrEmpty(_authKey) || _authenticated;
    }

    /// <summary>
    /// Checks if an IOException is a Windows socket closed error (WSARECV).
    /// Windows throws "WSARECV" errors on EOF, which are normal disconnects.
    /// </summary>
    private static bool IsWindowsSocketClosed(IOException ex)
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            ex.Message.Contains("WSA", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        _reader?.Dispose();
        _stream?.Dispose();
        _client.Dispose();
        _writeLock.Dispose();

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
