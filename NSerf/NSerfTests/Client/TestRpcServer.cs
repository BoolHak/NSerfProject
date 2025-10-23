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
        
        try
        {
            // Handle handshake
            var handshakeHeader = await MessagePackSerializer.DeserializeAsync<RequestHeader>(stream, cancellationToken: _cts.Token);
            
            if (handshakeHeader.Command == RpcCommands.Handshake)
            {
                var handshakeReq = await MessagePackSerializer.DeserializeAsync<HandshakeRequest>(stream, cancellationToken: _cts.Token);
                
                var handshakeResp = new ResponseHeader
                {
                    Seq = handshakeHeader.Seq,
                    Error = handshakeReq.Version > RpcConstants.MaxIPCVersion ? "Unsupported version" : string.Empty
                };
                
                await MessagePackSerializer.SerializeAsync(stream, handshakeResp, cancellationToken: _cts.Token);
                
                if (!string.IsNullOrEmpty(handshakeResp.Error))
                    return;
            }

            // Handle auth if required
            if (!string.IsNullOrEmpty(_expectedAuthKey))
            {
                var authHeader = await MessagePackSerializer.DeserializeAsync<RequestHeader>(stream, cancellationToken: _cts.Token);
                
                if (authHeader.Command == RpcCommands.Auth)
                {
                    var authReq = await MessagePackSerializer.DeserializeAsync<AuthRequest>(stream, cancellationToken: _cts.Token);
                    
                    var authResp = new ResponseHeader
                    {
                        Seq = authHeader.Seq,
                        Error = authReq.AuthKey == _expectedAuthKey ? string.Empty : "Invalid auth key"
                    };
                    
                    await MessagePackSerializer.SerializeAsync(stream, authResp, cancellationToken: _cts.Token);
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
