// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using MessagePack;

namespace NSerf.Client;

/// <summary>
/// RPC client for connecting to Serf agent.
/// Maps to: Go's client/rpc_client.go
/// </summary>
public class RpcClient(RpcConfig config) : IDisposable, IAsyncDisposable
{
    private readonly RpcConfig _config = config ?? throw new ArgumentNullException(nameof(config));
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private MessagePackStreamReader? _reader;
    private ulong _seqCounter;
    private bool _disposed;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // MessagePack options matching Go's codec
    private static readonly MessagePackSerializerOptions MsgPackOptions =
        MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.None);

    /// <summary>
    /// Connects to the Serf agent RPC endpoint.
    /// Maps to: Go's ClientFromConfig() in rpc_client.go
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_tcpClient != null)
            throw new InvalidOperationException("Already connected");

        try
        {
            _tcpClient = new TcpClient();

            // Parse address (format: "host:port")
            var parts = _config.Address.Split(':');
            if (parts.Length != 2)
                throw new ArgumentException($"Invalid address format: {_config.Address}. Expected 'host:port'");

            var host = parts[0];
            if (!int.TryParse(parts[1], out var port))
                throw new ArgumentException($"Invalid port: {parts[1]}");

            // Connect with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_config.Timeout);

            await _tcpClient.ConnectAsync(host, port, cts.Token);
            _tcpClient.NoDelay = true;
            _stream = _tcpClient.GetStream();
            _reader = new MessagePackStreamReader(_stream, leaveOpen: true);

            // Perform handshake
            await HandshakeAsync(cts.Token);

            // Perform authentication if auth key provided
            if (!string.IsNullOrEmpty(_config.AuthKey))
            {
                await AuthenticateAsync(_config.AuthKey, cts.Token);
            }
        }
        catch
        {
            // Cleanup on failure
            _reader?.Dispose();
            if(_stream != null)
                await _stream.DisposeAsync();
            _tcpClient?.Dispose();
            _reader = null;
            _stream = null;
            _tcpClient = null;
            throw;
        }
    }

    /// <summary>
    /// Performs the initial handshake with the agent.
    /// Maps to: Go's handshake() in rpc_client.go
    /// </summary>
    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader
        {
            Command = RpcCommands.Handshake,
            Seq = seq
        };
        var request = new HandshakeRequest
        {
            Version = RpcConstants.MaxIpcVersion
        };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Handshake failed: {response.Error}");
    }

    /// <summary>
    /// Performs authentication with the agent.
    /// Maps to: Go's auth() in rpc_client.go
    /// </summary>
    private async Task AuthenticateAsync(string authKey, CancellationToken cancellationToken)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader
        {
            Command = RpcCommands.Auth,
            Seq = seq
        };
        var request = new AuthRequest
        {
            AuthKey = authKey
        };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Authentication failed: {response.Error}");
    }

    /// <summary>
    /// Sends a request to the agent.
    /// Maps to: Go's send() in rpc_client.go
    /// </summary>
    private async Task SendRequestAsync<TRequest>(
        RequestHeader header,
        TRequest? request,
        CancellationToken cancellationToken)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // Set a write deadline (timeout)
            _tcpClient!.SendTimeout = (int)_config.Timeout.TotalMilliseconds;

            // Encode and send header
            var headerBytes = MessagePackSerializer.Serialize(header, MsgPackOptions, cancellationToken);
            await _stream.WriteAsync(headerBytes, cancellationToken);

            // Encode and send a request body if present
            if (!Equals(request, default(TRequest)))
            {
                var requestBytes = MessagePackSerializer.Serialize(request, MsgPackOptions, cancellationToken);
                await _stream.WriteAsync(requestBytes, cancellationToken);
            }

            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Receives a response from the agent.
    /// Maps to: Go's genericRPC() response handling in rpc_client.go
    /// </summary>
    private async Task<ResponseResult<TResponse>> ReceiveResponseAsync<TResponse>(
        ulong expectedSeq,
        CancellationToken cancellationToken)
    {
        if (_reader == null)
            throw new InvalidOperationException("Not connected");

        // Set the read deadline (timeout)
        _tcpClient!.ReceiveTimeout = (int)_config.Timeout.TotalMilliseconds;

        // Read the response header using MessagePackStreamReader
        var headerBytes = await _reader.ReadAsync(cancellationToken);
        if (!headerBytes.HasValue)
            throw new RpcException("Connection closed while reading response header");

        var responseHeader = MessagePackSerializer.Deserialize<ResponseHeader>(
            headerBytes.Value,
            MsgPackOptions,
            cancellationToken);

        // Verify sequence number matches
        if (responseHeader.Seq != expectedSeq)
            throw new RpcException($"Sequence mismatch: expected {expectedSeq}, got {responseHeader.Seq}");

        // Read the response body if no error and TResponse is not the object
        TResponse? responseBody = default;
        if (!string.IsNullOrEmpty(responseHeader.Error) || typeof(TResponse) == typeof(object))
            return new ResponseResult<TResponse>
            {
                Error = responseHeader.Error,
                Body = responseBody
            };
        var bodyBytes = await _reader.ReadAsync(cancellationToken);
        if (!bodyBytes.HasValue)
            throw new RpcException("Connection closed while reading response body");

        responseBody = MessagePackSerializer.Deserialize<TResponse>(
            bodyBytes.Value,
            MsgPackOptions,
            cancellationToken);

        return new ResponseResult<TResponse>
        {
            Error = responseHeader.Error,
            Body = responseBody
        };
    }

    private ulong GetNextSeq()
    {
        return Interlocked.Increment(ref _seqCounter);
    }

    public bool IsConnected => _tcpClient?.Connected ?? false;

    // ========== Membership Commands ==========

    public async Task<Responses.Member[]> MembersAsync(CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Members, Seq = seq };

        await SendRequestAsync<object>(header, null, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.MembersResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Members command failed: {response.Error}");

        return response.Body?.Members ?? [];
    }

    public async Task<Responses.Member[]> MembersFilteredAsync(
        Dictionary<string, string>? tags = null,
        string? status = null,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.MembersFiltered, Seq = seq };
        var request = new Requests.MembersFilteredRequest
        {
            Tags = tags ?? [],
            Status = status ?? string.Empty,
            Name = name ?? string.Empty
        };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.MembersResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"MembersFiltered command failed: {response.Error}");

        return response.Body?.Members ?? [];
    }

    public async Task<int> JoinAsync(string[] addresses, bool replay = false, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Join, Seq = seq };
        var request = new Requests.JoinRequest { Existing = addresses, Replay = replay };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.JoinResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Join command failed: {response.Error}");

        return response.Body?.Num ?? 0;
    }

    public async Task ForceLeaveAsync(string node, bool prune = false, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.ForceLeave, Seq = seq };
        var request = new Requests.ForceLeaveRequest { Node = node, Prune = prune };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"ForceLeave command failed: {response.Error}");
    }

    public async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Leave, Seq = seq };

        await SendRequestAsync<object>(header, null, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Leave command failed: {response.Error}");
    }

    // ========== Event Commands ==========

    public async Task UserEventAsync(string name, byte[]? payload = null, bool coalesce = false, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Event, Seq = seq };
        var request = new Requests.EventRequest
        {
            Name = name,
            Payload = payload ?? [],
            Coalesce = coalesce
        };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"UserEvent command failed: {response.Error}");
    }

    // ========== Key Management Commands ==========

    public async Task<Responses.KeyResponse> InstallKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.InstallKey, Seq = seq };
        var request = new Requests.KeyRequest { Key = key };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.KeyResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"InstallKey command failed: {response.Error}");

        return response.Body ?? new Responses.KeyResponse();
    }

    public async Task<Responses.KeyResponse> UseKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.UseKey, Seq = seq };
        var request = new Requests.KeyRequest { Key = key };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.KeyResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"UseKey command failed: {response.Error}");

        return response.Body ?? new Responses.KeyResponse();
    }

    public async Task<Responses.KeyResponse> RemoveKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.RemoveKey, Seq = seq };
        var request = new Requests.KeyRequest { Key = key };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.KeyResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"RemoveKey command failed: {response.Error}");

        return response.Body ?? new Responses.KeyResponse();
    }

    public async Task<Responses.KeyResponse> ListKeysAsync(CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.ListKeys, Seq = seq };

        await SendRequestAsync(header, (object?)null, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.KeyResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"ListKeys command failed: {response.Error}");

        return response.Body ?? new Responses.KeyResponse();
    }

    // ========== Query Commands ==========

    public async Task<ulong> QueryAsync(
        string name,
        byte[]? payload = null,
        string? filterNodes = null,
        Dictionary<string, string>? filterTags = null,
        bool requestAck = false,
        uint timeoutSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Query, Seq = seq };
        var request = new Requests.QueryRequest
        {
            Name = name,
            Payload = payload ?? [],
            FilterNodes = filterNodes ?? string.Empty,
            FilterTags = filterTags != null ? string.Join(",", filterTags.Select(kv => $"{kv.Key}={kv.Value}")) : string.Empty,
            RequestAck = requestAck,
            Timeout = timeoutSeconds
        };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.QueryResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Query command failed: {response.Error}");

        return response.Body?.Id ?? 0;
    }

    public async Task RespondAsync(ulong queryId, byte[] payload, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Respond, Seq = seq };
        var request = new Requests.RespondRequest { ID = queryId, Payload = payload };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Respond command failed: {response.Error}");
    }

    // ========== Other Commands ==========

    public async Task UpdateTagsAsync(Dictionary<string, string>? tags = null, string[]? deleteTags = null, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Tags, Seq = seq };
        var request = new Requests.TagsRequest
        {
            Tags = tags ?? [],
            DeleteTags = deleteTags ?? []
        };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"UpdateTags command failed: {response.Error}");
    }

    public async Task<Dictionary<string, Dictionary<string, string>>> StatsAsync(CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Stats, Seq = seq };

        await SendRequestAsync(header, (object?)null, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.StatsResponse>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Stats command failed: {response.Error}");

        return response.Body?.Stats ?? [];
    }

    public async Task<Responses.Coordinate?> GetCoordinateAsync(string node, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.GetCoordinate, Seq = seq };
        var request = new Requests.CoordinateRequest { Node = node };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<Responses.CoordinateResponse>(seq, cancellationToken);

        return !string.IsNullOrEmpty(response.Error) ? 
            throw new RpcException($"GetCoordinate command failed: {response.Error}") : 
            response.Body?.Coord;
    }

    // ========== Streaming Commands ==========

    public async IAsyncEnumerable<string> MonitorAsync(
        string logLevel = "INFO",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Monitor, Seq = seq };
        var request = new Requests.MonitorRequest { LogLevel = logLevel };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Monitor command failed: {response.Error}");

        while (!cancellationToken.IsCancellationRequested)
        {
            Responses.LogEntry? logEntry;
            try
            {
                var logBytes = await _reader!.ReadAsync(cancellationToken);
                if (!logBytes.HasValue)
                    break;

                logEntry = MessagePackSerializer.Deserialize<Responses.LogEntry>(
                    logBytes.Value,
                    MsgPackOptions,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            yield return logEntry.Log;
        }
    }

    public async IAsyncEnumerable<Responses.StreamEvent> StreamAsync(
        string type = "*",
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Stream, Seq = seq };
        var request = new Requests.StreamRequest { Type = type };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Stream command failed: {response.Error}");

        while (!cancellationToken.IsCancellationRequested)
        {
            Responses.StreamEvent? streamEvent;
            try
            {
                var eventBytes = await _reader!.ReadAsync(cancellationToken);
                if (!eventBytes.HasValue)
                    break;

                streamEvent = MessagePackSerializer.Deserialize<Responses.StreamEvent>(
                    eventBytes.Value,
                    MsgPackOptions,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            yield return streamEvent;
        }
    }

    public async Task StopAsync(ulong stopSeq, CancellationToken cancellationToken = default)
    {
        var seq = GetNextSeq();
        var header = new RequestHeader { Command = RpcCommands.Stop, Seq = seq };
        var request = new Requests.StopRequest { Stop = stopSeq };

        await SendRequestAsync(header, request, cancellationToken);
        var response = await ReceiveResponseAsync<object>(seq, cancellationToken);

        if (!string.IsNullOrEmpty(response.Error))
            throw new RpcException($"Stop command failed: {response.Error}");
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Dispose managed resources
            _reader?.Dispose();
            _stream?.Dispose();
            _tcpClient?.Dispose();
            _writeLock.Dispose();
        }

        _disposed = true;
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed) return;

        _reader?.Dispose();
        if (_stream != null)
        {
            await _stream.DisposeAsync();
        }
        _tcpClient?.Dispose();
        _writeLock.Dispose();

        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        // Ensure a synchronous cleanup path is also marked as disposed
        Dispose(false);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Internal result type for responses
/// </summary>
internal class ResponseResult<T>
{
    public string Error { get; set; } = string.Empty;
    public T? Body { get; set; }
}
