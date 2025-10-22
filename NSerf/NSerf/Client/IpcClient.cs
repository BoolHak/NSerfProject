using System.Net.Sockets;
using System.Threading.Channels;
using MessagePack;
using System.Collections.Concurrent;

namespace NSerf.Client;

public class StreamHandle
{
    public ulong Seq { get; init; }
}

public class IpcClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private MessagePackStreamReader? _reader;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly MessagePackSerializerOptions _options;
    private readonly ConcurrentDictionary<ulong, IResponseHandler> _handlers = new();
    private Task? _backgroundReaderTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;

    public IpcClient(MessagePackSerializerOptions? options = null)
    {
        _options = options ?? MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, cancellationToken);
        _stream = _tcpClient.GetStream();
        _reader = new MessagePackStreamReader(_stream, leaveOpen: true);
        // Background reader will be started on first streaming command
    }

    public async Task<ResponseHeader> HandshakeAsync(int version, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.HandshakeCommand, Seq = 1 };
        var body = new HandshakeRequest { Version = version };
        await SendAsync(header, body, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<ResponseHeader> AuthAsync(string authKey, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.AuthCommand, Seq = seq };
        var body = new AuthRequest { AuthKey = authKey };
        await SendAsync(header, body, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<ResponseHeader> SendHeaderOnlyAsync(string command, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = command, Seq = seq };
        await SendAsync(header, null, cancellationToken);
        var response = await ReadResponseAsync(cancellationToken);
        
        // If server sent a body, we need to consume it to keep stream in sync
        // For header-only requests, we just discard the body
        if (!string.IsNullOrEmpty(response.Error))
        {
            // Error responses don't have bodies
            return response;
        }
        
        // Check if there's a body by peeking at the next message
        // For now, always try to read and discard one more message for commands that return bodies
        // TODO: Make this smarter based on command type
        if (command == IpcProtocol.MembersCommand || command == IpcProtocol.StatsCommand)
        {
            // These commands return bodies, consume them
            await ReadAndDiscardBodyAsync(cancellationToken);
        }
        
        return response;
    }
    
    private async Task ReadAndDiscardBodyAsync(CancellationToken cancellationToken)
    {
        if (_reader is null) throw new InvalidOperationException("Not connected");
        // Read and discard the body message
        await _reader.ReadAsync(cancellationToken);
    }

    public async Task<(ResponseHeader, MembersResponse)> GetMembersAsync(ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.MembersCommand, Seq = seq };
        await SendAsync(header, null, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new MembersResponse { Members = Array.Empty<IpcMember>() });
        }
        
        var body = await ReadBodyAsync<MembersResponse>(cancellationToken);
        return (responseHeader, body);
    }

    public async Task<(ResponseHeader, Dictionary<string, Dictionary<string, string>>)> GetStatsAsync(ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.StatsCommand, Seq = seq };
        await SendAsync(header, null, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new Dictionary<string, Dictionary<string, string>>());
        }
        
        var body = await ReadBodyAsync<Dictionary<string, Dictionary<string, string>>>(cancellationToken);
        return (responseHeader, body);
    }

    public async Task<(ResponseHeader, JoinResponse)> JoinAsync(string[] existing, bool replay, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.JoinCommand, Seq = seq };
        var body = new JoinRequest { Existing = existing, Replay = replay };
        await SendAsync(header, body, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new JoinResponse { Num = 0 });
        }
        
        var responseBody = await ReadBodyAsync<JoinResponse>(cancellationToken);
        return (responseHeader, responseBody);
    }

    public async Task<ResponseHeader> LeaveAsync(ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.LeaveCommand, Seq = seq };
        await SendAsync(header, null, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<ResponseHeader> ForceLeaveAsync(string node, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.ForceLeaveCommand, Seq = seq };
        var body = new ForceLeaveRequest { Node = node };
        await SendAsync(header, body, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<ResponseHeader> UserEventAsync(string name, byte[] payload, bool coalesce, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.EventCommand, Seq = seq };
        var body = new EventRequest { Name = name, Payload = payload, Coalesce = coalesce };
        await SendAsync(header, body, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<ResponseHeader> SetTagsAsync(Dictionary<string, string> tags, string[]? deleteTags, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.TagsCommand, Seq = seq };
        var body = new TagsRequest { Tags = tags, DeleteTags = deleteTags };
        await SendAsync(header, body, cancellationToken);
        return await ReadResponseAsync(cancellationToken);
    }

    public async Task<(ResponseHeader, KeyResponse)> InstallKeyAsync(string key, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.InstallKeyCommand, Seq = seq };
        var body = new KeyRequest { Key = key };
        await SendAsync(header, body, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new KeyResponse());
        }
        
        var responseBody = await ReadBodyAsync<KeyResponse>(cancellationToken);
        return (responseHeader, responseBody);
    }

    public async Task<(ResponseHeader, KeyResponse)> UseKeyAsync(string key, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.UseKeyCommand, Seq = seq };
        var body = new KeyRequest { Key = key };
        await SendAsync(header, body, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new KeyResponse());
        }
        
        var responseBody = await ReadBodyAsync<KeyResponse>(cancellationToken);
        return (responseHeader, responseBody);
    }

    public async Task<(ResponseHeader, KeyResponse)> RemoveKeyAsync(string key, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.RemoveKeyCommand, Seq = seq };
        var body = new KeyRequest { Key = key };
        await SendAsync(header, body, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new KeyResponse());
        }
        
        var responseBody = await ReadBodyAsync<KeyResponse>(cancellationToken);
        return (responseHeader, responseBody);
    }

    public async Task<(ResponseHeader, KeyResponse)> ListKeysAsync(ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new RequestHeader { Command = IpcProtocol.ListKeysCommand, Seq = seq };
        await SendAsync(header, null, cancellationToken);
        var responseHeader = await ReadResponseAsync(cancellationToken);
        
        if (!string.IsNullOrEmpty(responseHeader.Error))
        {
            return (responseHeader, new KeyResponse());
        }
        
        var responseBody = await ReadBodyAsync<KeyResponse>(cancellationToken);
        return (responseHeader, responseBody);
    }

    public async Task<StreamHandle> MonitorAsync(string logLevel, ChannelWriter<string> logWriter, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        EnsureBackgroundReaderStarted(); // Start reader on first streaming command
        
        // Register handler BEFORE sending request
        var handler = new MonitorHandler(logWriter);
        _handlers[seq] = handler;
        
        var header = new RequestHeader { Command = IpcProtocol.MonitorCommand, Seq = seq };
        var body = new MonitorRequest { LogLevel = logLevel };
        await SendAsync(header, body, cancellationToken);
        
        // Wait for initialization response
        try
        {
            await handler.InitTask;
        }
        catch
        {
            _handlers.TryRemove(seq, out _);
            throw;
        }
        
        return new StreamHandle { Seq = seq };
    }

    public async Task<StreamHandle> StreamAsync(string eventType, ChannelWriter<Dictionary<string, object>> eventWriter, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        EnsureBackgroundReaderStarted(); // Start reader on first streaming command
        
        // Register handler BEFORE sending request
        var handler = new StreamHandler(eventWriter);
        _handlers[seq] = handler;
        
        var header = new RequestHeader { Command = IpcProtocol.StreamCommand, Seq = seq };
        var body = new StreamRequest { Type = eventType };
        await SendAsync(header, body, cancellationToken);
        
        // Wait for initialization response
        try
        {
            await handler.InitTask;
        }
        catch
        {
            _handlers.TryRemove(seq, out _);
            throw;
        }
        
        return new StreamHandle { Seq = seq };
    }

    public async Task<ResponseHeader> StopAsync(ulong stopSeq, ulong seq, CancellationToken cancellationToken)
    {
        EnsureConnected();
        
        // If background reader is running, use callback handler pattern
        if (_backgroundReaderTask != null)
        {
            // StopAsync returns no body, just header
            var callbackHandler = new CallbackHandler(_options, expectBody: false);
            _handlers[seq] = callbackHandler;
            
            var header = new RequestHeader { Command = IpcProtocol.StopCommand, Seq = seq };
            var body = new StopRequest { Stop = stopSeq };
            await SendAsync(header, body, cancellationToken);
            
            // Wait for response via callback
            var (responseHeader, _) = await callbackHandler.Task;
            
            // Deregister this command's handler
            _handlers.TryRemove(seq, out _);
            
            // Deregister and cleanup the stream handler
            if (_handlers.TryRemove(stopSeq, out var streamHandler))
            {
                await streamHandler.CleanupAsync();
            }
            
            return responseHeader;
        }
        else
        {
            // No background reader, use direct read
            var header = new RequestHeader { Command = IpcProtocol.StopCommand, Seq = seq };
            var body = new StopRequest { Stop = stopSeq };
            await SendAsync(header, body, cancellationToken);
            var response = await ReadResponseAsync(cancellationToken);
            
            // Deregister and cleanup the handler
            if (_handlers.TryRemove(stopSeq, out var handler))
            {
                await handler.CleanupAsync();
            }
            
            return response;
        }
    }

    private async Task<T> ReadBodyAsync<T>(CancellationToken cancellationToken)
    {
        if (_reader is null) throw new InvalidOperationException("Not connected");
        var msg = await _reader.ReadAsync(cancellationToken);
        if (!msg.HasValue) throw new EndOfStreamException();
        return MessagePackSerializer.Deserialize<T>(msg.Value, _options);
    }

    private async Task SendAsync(object header, object? body, CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await MessagePackSerializer.SerializeAsync(_stream, header, _options, cancellationToken);
            if (body is not null)
            {
                var type = body.GetType();
                await MessagePackSerializer.SerializeAsync(type, _stream, body, _options, cancellationToken);
            }
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<ResponseHeader> ReadResponseAsync(CancellationToken cancellationToken)
    {
        if (_reader is null) throw new InvalidOperationException("Not connected");
        var msg = await _reader.ReadAsync(cancellationToken);
        if (!msg.HasValue) throw new EndOfStreamException();
        return MessagePackSerializer.Deserialize<ResponseHeader>(msg.Value, _options);
    }

    private void EnsureConnected()
    {
        if (_stream is null || _reader is null)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }
    }

    private void EnsureBackgroundReaderStarted()
    {
        if (_backgroundReaderTask == null && !_disposed)
        {
            _backgroundReaderTask = Task.Run(async () => await BackgroundReaderLoopAsync(_disposeCts.Token), _disposeCts.Token);
        }
    }

    /// <summary>
    /// Background reader loop that continuously reads responses and dispatches to handlers.
    /// Based on Go's listen() goroutine pattern.
    /// </summary>
    private async Task BackgroundReaderLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader != null)
            {
                try
                {
                    var msgpack = await _reader.ReadAsync(cancellationToken);
                    if (!msgpack.HasValue)
                    {
                        break; // Stream closed
                    }

                    var header = MessagePackSerializer.Deserialize<ResponseHeader>(msgpack.Value, _options);

                    // Check if there's a handler registered for this sequence
                    if (_handlers.TryGetValue(header.Seq, out var handler))
                    {
                        // Dispatch to handler
                        await handler.HandleAsync(header, _reader);
                    }
                    // else: Response for non-streaming command, will be read by ReadResponseAsync
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Error reading, likely connection closed
                    break;
                }
            }
        }
        finally
        {
            // Cleanup all handlers when reader stops
            foreach (var kvp in _handlers)
            {
                try
                {
                    await kvp.Value.CleanupAsync();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            _handlers.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Signal background reader to stop
        _disposeCts.Cancel();

        // Wait for background reader to finish
        if (_backgroundReaderTask != null)
        {
            try
            {
                await _backgroundReaderTask;
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        // Cleanup all remaining handlers
        foreach (var kvp in _handlers.ToArray())
        {
            try
            {
                await kvp.Value.CleanupAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        _handlers.Clear();

        _stream?.Dispose();
        _tcpClient?.Dispose();
        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
