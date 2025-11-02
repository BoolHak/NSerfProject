// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Net.Sockets;

namespace NSerf.Memberlist;

/// <summary>
/// Pool of TCP connections for reuse.
/// </summary>
public class ConnectionPool(TimeSpan maxAge, int maxPerHost = 10) : IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TcpClient>> _pools = new();
    private readonly TimeSpan _maxAge = maxAge;
    private readonly int _maxPerHost = maxPerHost;
    private bool _disposed;

    /// <summary>
    /// Gets or creates a connection to the specified host.
    /// </summary>
    public async Task<TcpClient> GetConnectionAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var key = $"{host}:{port}";

        if (_pools.TryGetValue(key, out var queue) && queue.TryDequeue(out var client))
        {
            if (client.Connected)
            {
                return client;
            }
            client.Dispose();
        }

        // Create new connection
        var newClient = new TcpClient();
        await newClient.ConnectAsync(host, port, cancellationToken);
        return newClient;
    }

    /// <summary>
    /// Returns a connection to the pool.
    /// </summary>
    public void ReturnConnection(string host, int port, TcpClient client)
    {
        if (_disposed || !client.Connected)
        {
            client.Dispose();
            return;
        }

        var key = $"{host}:{port}";
        var queue = _pools.GetOrAdd(key, _ => new ConcurrentQueue<TcpClient>());

        if (queue.Count < _maxPerHost)
        {
            queue.Enqueue(client);
        }
        else
        {
            client.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var queue in _pools.Values)
        {
            while (queue.TryDequeue(out var client))
            {
                client.Dispose();
            }
        }

        _pools.Clear();
    }
}
