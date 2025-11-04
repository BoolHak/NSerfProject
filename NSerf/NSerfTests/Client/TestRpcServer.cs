using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using NSerf.Client;

namespace NSerfTests.Client;

/// <summary>
/// Minimal test RPC server for testing handshake and auth
/// </summary>
public class TestRpcServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly string? _expectedAuthKey;
    private Task? _acceptTask;
    
    public int Port { get; }
    public bool IsRunning => !_cts.IsCancellationRequested;

    public TestRpcServer(int port = 0, string? expectedAuthKey = null)
    {
        _expectedAuthKey = expectedAuthKey;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        _acceptTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        });
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        using var reader = new MessagePackStreamReader(stream, leaveOpen: true);
        
        try
        {
            // Handle handshake - use MessagePackStreamReader like RpcSession does
            var headerBytes = await reader.ReadAsync(_cts.Token);
            if (!headerBytes.HasValue) return;
            
            var handshakeHeader = MessagePackSerializer.Deserialize<RequestHeader>(headerBytes.Value);
            
            if (handshakeHeader.Command == RpcCommands.Handshake)
            {
                var reqBytes = await reader.ReadAsync(_cts.Token);
                if (!reqBytes.HasValue) return;
                
                var handshakeReq = MessagePackSerializer.Deserialize<HandshakeRequest>(reqBytes.Value);
                
                var handshakeResp = new ResponseHeader
                {
                    Seq = handshakeHeader.Seq,
                    Error = handshakeReq.Version > RpcConstants.MaxIpcVersion ? "Unsupported version" : string.Empty
                };
                
                var responseBytes = MessagePackSerializer.Serialize(handshakeResp);
                await stream.WriteAsync(responseBytes, _cts.Token);
                await stream.FlushAsync(_cts.Token);
                
                if (!string.IsNullOrEmpty(handshakeResp.Error))
                    return;
            }

            // Handle auth if required
            if (!string.IsNullOrEmpty(_expectedAuthKey))
            {
                var authHeaderBytes = await reader.ReadAsync(_cts.Token);
                if (!authHeaderBytes.HasValue) return;
                
                var authHeader = MessagePackSerializer.Deserialize<RequestHeader>(authHeaderBytes.Value);
                
                if (authHeader.Command == RpcCommands.Auth)
                {
                    var authReqBytes = await reader.ReadAsync(_cts.Token);
                    if (!authReqBytes.HasValue) return;
                    
                    var authReq = MessagePackSerializer.Deserialize<AuthRequest>(authReqBytes.Value);
                    
                    var authResp = new ResponseHeader
                    {
                        Seq = authHeader.Seq,
                        Error = authReq.AuthKey == _expectedAuthKey ? string.Empty : "Invalid auth key"
                    };
                    
                    var authRespBytes = MessagePackSerializer.Serialize(authResp);
                    await stream.WriteAsync(authRespBytes, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
            }
            
            // Connection handled successfully - client can now send commands
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _acceptTask?.Wait(TimeSpan.FromSeconds(1));
        _cts.Dispose();
    }
}
