using MessagePack;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using NSerf.Serf;

namespace NSerf.Client;

/// <summary>
/// IPC server for Serf agent - handles client connections and command routing.
/// </summary>
public partial class AgentIpc : IAsyncDisposable
{
    private readonly Serf.Serf _serf;
    private readonly string? _authKey;
    private readonly TcpListener _listener;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, IpcClientHandler> _clients = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly MessagePackSerializerOptions _serializerOptions;
    private Task? _listenTask;
    
    // Phase 16: Event and Log streaming
    private readonly EventStreamManager? _eventStreamManager;
    private readonly LogStreamManager? _logStreamManager;
    
    /// <summary>
    /// Gets the port the server is listening on.
    /// </summary>
    public int Port { get; private set; }
    
    public AgentIpc(
        Serf.Serf serf, 
        string address, 
        string? authKey = null,
        bool msgpackUseNewTimeFormat = false,
        ILogger? logger = null)
    {
        _serf = serf;
        _authKey = authKey;
        _logger = logger;
        
        var parts = address.Split(':');
        var ipAddress = parts[0] == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(parts[0]);
        var port = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        
        _listener = new TcpListener(ipAddress, port);
        
        _serializerOptions = MessagePackSerializerOptions.Standard
            .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
        
        // Phase 16: Initialize EventStreamManager with Serf's IPC event reader
        _eventStreamManager = new EventStreamManager(serf.IpcEventReader);
        
        // Phase 16: Initialize LogStreamManager
        if (serf.Logger != null)
        {
            _logStreamManager = new LogStreamManager(serf.Logger);
            if (serf.Logger is not ILoggableLogger)
            {
                _logger?.LogWarning("Serf.Logger is not ILoggableLogger, log streaming hook may not work");
            }
        }
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        
        // Phase 16: Start EventStreamManager background task
        _eventStreamManager?.Start(_shutdownCts.Token);
        
        _listenTask = Task.Run(() => ListenAsync(_shutdownCts.Token), _shutdownCts.Token);
        
        // Give the listen task a moment to start
        await Task.Delay(10, cancellationToken);
    }
    
    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[AgentIpc] ListenAsync started");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.WriteLine("[AgentIpc] Waiting for client...");
                var tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken);
                Console.WriteLine("[AgentIpc] Accepted client connection");
                _logger?.LogInformation("Accepted client connection");
                tcpClient.NoDelay = true; // Disable Nagle's algorithm for faster response
                var clientName = tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";
                var client = new IpcClientHandler(clientName, tcpClient, _serializerOptions);
                
                _clients[client.Name] = client;
                Console.WriteLine($"[AgentIpc] Starting handler for {clientName}");
                _logger?.LogInformation("Starting handler for {ClientName}", client.Name);
                _ = Task.Run(async () => await HandleClientAsync(client, _shutdownCts.Token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error accepting client connection");
            }
        }
    }
    
    private async Task HandleClientAsync(IpcClientHandler client, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[AgentIpc] HandleClientAsync started for {client.Name}");
        _logger?.LogInformation("HandleClientAsync started for {ClientName}", client.Name);
        
        // Use MessagePackStreamReader for proper bidirectional communication
        using var reader = new MessagePackStreamReader(client.GetStream(), leaveOpen: true);
        
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"[AgentIpc] Waiting for request from {client.Name}");
                _logger?.LogDebug("Waiting for request from {ClientName}", client.Name);
                RequestHeader? header = null;
                try
                {
                    Console.WriteLine($"[AgentIpc] Reading from MessagePackStreamReader...");
                    var msgpack = await reader.ReadAsync(cancellationToken);
                    if (!msgpack.HasValue)
                    {
                        Console.WriteLine($"[AgentIpc] Client {client.Name} closed connection (no data)");
                        _logger?.LogDebug("Client {ClientName} closed connection", client.Name);
                        break;
                    }
                    
                    header = MessagePackSerializer.Deserialize<RequestHeader>(msgpack.Value, _serializerOptions);
                    Console.WriteLine($"[AgentIpc] Received command: {header.Command}, Seq: {header.Seq}");
                    _logger?.LogDebug("Received command: {Command}, Seq: {Seq}", header.Command, header.Seq);
                }
                catch (EndOfStreamException ex)
                {
                    _logger?.LogDebug("Client {ClientName} closed connection: {Error}", client.Name, ex.Message);
                    break;
                }
                catch (MessagePackSerializationException ex)
                {
                    _logger?.LogWarning("Invalid data from {ClientName}: {Error}", client.Name, ex.Message);
                    break;
                }
                
                if (header == null)
                {
                    _logger?.LogWarning("Null header from {ClientName}", client.Name);
                    break;
                }
                
                await HandleRequestAsync(client, header, reader, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Client handler cancelled for {ClientName}", client.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Client {ClientName} error", client.Name);
        }
        finally
        {
            _logger?.LogDebug("Cleaning up client {ClientName}", client.Name);
            DeregisterClient(client);
        }
    }
    
    private async Task HandleRequestAsync(IpcClientHandler client, RequestHeader header, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[AgentIpc] HandleRequestAsync: Command={header.Command}, Seq={header.Seq}, Version={client.Version}");
        // Enforce handshake
        if (header.Command != IpcProtocol.HandshakeCommand && client.Version == 0)
        {
            Console.WriteLine($"[AgentIpc] Handshake required, sending error response");
            var resp = new ResponseHeader { Seq = header.Seq, Error = IpcProtocol.HandshakeRequired };
            await client.SendAsync(resp, null, cancellationToken);
            Console.WriteLine($"[AgentIpc] Error response sent");
            return;
        }
        
        // Enforce auth
        if (!string.IsNullOrEmpty(_authKey) && !client.DidAuth && 
            header.Command != IpcProtocol.HandshakeCommand && 
            header.Command != IpcProtocol.AuthCommand)
        {
            var resp = new ResponseHeader { Seq = header.Seq, Error = IpcProtocol.AuthRequired };
            await client.SendAsync(resp, null, cancellationToken);
            return;
        }
        
        // Route command
        await (header.Command switch
        {
            IpcProtocol.HandshakeCommand => HandleHandshakeAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.AuthCommand => HandleAuthAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.MembersCommand => HandleMembersAsync(client, header.Seq, cancellationToken),
            IpcProtocol.MembersFilteredCommand => HandleMembersFilteredAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.JoinCommand => HandleJoinAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.LeaveCommand => HandleLeaveAsync(client, header.Seq, cancellationToken),
            IpcProtocol.ForceLeaveCommand => HandleForceLeaveAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.EventCommand => HandleUserEventAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.TagsCommand => HandleTagsAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.StatsCommand => HandleStatsAsync(client, header.Seq, cancellationToken),
            IpcProtocol.GetCoordinateCommand => HandleGetCoordinateAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.InstallKeyCommand => HandleInstallKeyAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.UseKeyCommand => HandleUseKeyAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.RemoveKeyCommand => HandleRemoveKeyAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.ListKeysCommand => HandleListKeysAsync(client, header.Seq, cancellationToken),
            IpcProtocol.MonitorCommand => HandleMonitorAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.StreamCommand => HandleStreamAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.StopCommand => HandleStopAsync(client, header.Seq, reader, cancellationToken),
            IpcProtocol.RespondCommand => HandleRespondAsync(client, header.Seq, reader, cancellationToken),
            _ => HandleUnsupportedCommandAsync(client, header.Seq, cancellationToken)
        });
    }
    
    private async Task HandleHandshakeAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<HandshakeRequest>(msgpack!.Value, _serializerOptions);
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        
        if (req.Version < IpcProtocol.MinVersion || req.Version > IpcProtocol.MaxVersion)
        {
            resp.Error = IpcProtocol.UnsupportedIPCVersion;
        }
        else if (client.Version != 0)
        {
            resp.Error = IpcProtocol.DuplicateHandshake;
        }
        else
        {
            client.Version = req.Version;
        }
        
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private async Task HandleAuthAsync(IpcClientHandler client, ulong seq, MessagePackStreamReader reader, CancellationToken cancellationToken)
    {
        var msgpack = await reader.ReadAsync(cancellationToken);
        var req = MessagePackSerializer.Deserialize<AuthRequest>(msgpack!.Value, _serializerOptions);
        
        var resp = new ResponseHeader { Seq = seq, Error = "" };
        
        if (req.AuthKey == _authKey)
        {
            client.DidAuth = true;
        }
        else
        {
            resp.Error = IpcProtocol.InvalidAuthToken;
        }
        
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private async Task HandleUnsupportedCommandAsync(IpcClientHandler client, ulong seq, CancellationToken cancellationToken)
    {
        var resp = new ResponseHeader { Seq = seq, Error = IpcProtocol.UnsupportedCommand };
        await client.SendAsync(resp, null, cancellationToken);
    }
    
    private void DeregisterClient(IpcClientHandler client)
    {
        _clients.TryRemove(client.Name, out _);
        _ = client.DisposeAsync();
    }
    
    public async ValueTask DisposeAsync()
    {
        _shutdownCts.Cancel();
        _listener.Stop();
        
        if (_listenTask != null)
        {
            try
            {
                await _listenTask;
            }
            catch { }
        }
        
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }
        
        _shutdownCts.Dispose();
    }
}
