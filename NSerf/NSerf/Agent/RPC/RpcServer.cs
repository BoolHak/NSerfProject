// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;
using MessagePack;
using NSerf.Client;

namespace NSerf.Agent.RPC;

public class RpcServer(SerfAgent agent, string bindAddr, string? authKey = null) : IAsyncDisposable
{
    private readonly SerfAgent _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    private readonly string _bindAddr = bindAddr ?? throw new ArgumentNullException(nameof(bindAddr));
    private readonly string? _authKey = authKey;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly List<RpcSession> _sessions = [];
    private readonly object _sessionsLock = new();
    private bool _stopped;

    public string? Address { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        var parts = _bindAddr.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            throw new ArgumentException($"Invalid RPC address: {_bindAddr}");

        var addr = IPAddress.Parse(parts[0]);
        _listener = new TcpListener(addr, port);
        _listener.Start();

        // Get actual bound port (important for port 0)
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        Address = $"{endpoint.Address}:{endpoint.Port}";

        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptClientsAsync(_cts.Token), cancellationToken);

        return Task.CompletedTask;
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);

                // CRITICAL: Check if stopped before registering (race condition)
                lock (_sessionsLock)
                {
                    if (_stopped)
                    {
                        client.Close();  // Close immediately if already stopping
                        return;
                    }

                    var session = new RpcSession(_agent, client, _authKey);
                    _sessions.Add(session);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await session.HandleAsync(cancellationToken);
                        }
                        finally
                        {
                            // Cleanup: dispose session first, then remove from list
                            await session.DisposeAsync();

                            lock (_sessionsLock)
                            {
                                _sessions.Remove(session);
                            }
                        }
                    }, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Log error if logger available
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Set stop flag FIRST (under lock)
        lock (_sessionsLock)
        {
            _stopped = true;
        }

        // Stop accepting new connections
        _cts?.Cancel();
        _cts?.Dispose();
        _listener?.Stop();

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Close all existing sessions
        var disposeTasks = new List<Task>();
        lock (_sessionsLock)
        {
            foreach (var session in _sessions)
            {
                disposeTasks.Add(session.DisposeAsync().AsTask());
            }
            _sessions.Clear();
        }

        // Await all dispose operations
        await Task.WhenAll(disposeTasks);

        GC.SuppressFinalize(this);
    }


}
