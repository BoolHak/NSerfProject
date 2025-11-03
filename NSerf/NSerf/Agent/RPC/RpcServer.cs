// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;

namespace NSerf.Agent.RPC;

public class RpcServer(SerfAgent agent, string bindAddr, string? authKey = null) : IAsyncDisposable
{
    private readonly SerfAgent _agent = agent ?? throw new ArgumentNullException(nameof(agent));
    private readonly string _bindAddr = bindAddr ?? throw new ArgumentNullException(nameof(bindAddr));
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private readonly List<RpcSession> _sessions = [];
    private readonly object _sessionsLock = new();
    private bool _stopped;
    private bool _disposed;

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

                    var session = new RpcSession(_agent, client, authKey);
                    _sessions.Add(session);

                    _ = Task.Factory.StartNew(async () =>
                    {
                        try
                        {
                            await session.HandleAsync(cancellationToken);
                        }
                        finally
                        {
                            // Cleanup: dispose session first, then remove from a list
                            await session.DisposeAsync();

                            lock (_sessionsLock)
                            {
                                _sessions.Remove(session);
                            }
                        }
                    }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
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
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Set the stop flag FIRST (under lock)
        lock (_sessionsLock)
        {
            _stopped = true;
        }

        // Stop listener to unblock AcceptTcpClientAsync immediately
        _listener?.Stop();

        // Safely capture and clear CTS, then cancel to break loops
        var cts = Interlocked.Exchange(ref _cts, null);
        try
        {
            if (cts != null)
            {
                await cts.CancelAsync();
            }
        }
        catch (ObjectDisposedException)
        {
            // Ignore if CTS was already disposed of elsewhere
        }

        // Wait for the acceptance loop to end before disposing CTS
        var acceptTask = _acceptTask;
        if (acceptTask != null)
        {
            try
            {
                await acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Now it's safe to dispose of the CTS
        cts?.Dispose();

        // Close all existing sessions
        var disposeTasks = new List<Task>();
        lock (_sessionsLock)
        {
            disposeTasks.AddRange(_sessions.Select(session => session.DisposeAsync().AsTask()));
            _sessions.Clear();
        }

        // Await all dispose operations
        await Task.WhenAll(disposeTasks);

        GC.SuppressFinalize(this);
    }


}
