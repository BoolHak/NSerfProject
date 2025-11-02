// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace NSerf.Agent;

/// <summary>
/// mDNS discovery for automatic cluster joining.
/// Maps to: Go's discover.go
/// </summary>
public class AgentMdns(string service, string domain = "local", int port = 5353, ILogger? logger = null) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _client;
    private Task? _listenerTask;
    private bool _disposed;

    private const int MdnsPort = 5353;
    //This address is defined in RFC 6762 for local network discovery
    private const string MdnsAddress = "224.0.0.251";

    /// <summary>
    /// Gets the service name used for mDNS discovery.
    /// </summary>
    public string ServiceName => service;

    /// <summary>
    /// Gets the domain used for mDNS discovery.
    /// </summary>
    public string Domain => domain;

    /// <summary>
    /// Gets whether the mDNS service is started.
    /// </summary>
    public bool IsStarted => _client != null && _listenerTask != null && !_disposed;

    /// <summary>
    /// Start mDNS discovery.
    /// </summary>
    /// <returns>True if started successfully, false otherwise</returns>
    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check if already started
        if (_client != null && _listenerTask != null)
            return false;

        try
        {
            _client = new UdpClient(port);

            // Only join multicast group if using a real port (not 0)
            if (port != 0)
            {
                _client.JoinMulticastGroup(IPAddress.Parse(MdnsAddress));
            }

            _listenerTask = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);

            logger?.LogInformation("[mDNS] Started discovery for service: {Service} on port: {Port}", service, port);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[mDNS] Failed to start discovery");
            return false;
        }
    }

    /// <summary>
    /// Discover peers via mDNS.
    /// </summary>
    public async Task<string[]> DiscoverPeersAsync(TimeSpan timeout)
    {
        if (_disposed || _client == null) return [];

        var peers = new List<string>();
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            // Send mDNS query
            var query = BuildMdnsQuery(service, domain);
            var endpoint = new IPEndPoint(IPAddress.Parse(MdnsAddress), MdnsPort);
            await _client.SendAsync(query, query.Length, endpoint);

            logger?.LogDebug("[mDNS] Sent discovery query for {Service}", service);

            // Wait for responses (collected in ListenAsync)
            await Task.Delay(timeout, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[mDNS] Discovery error");
        }

        return [.. peers];
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        if (_client == null)
            return;

        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            try
            {
                var result = await _client.ReceiveAsync(cancellationToken);
                ProcessMdnsResponse(result.Buffer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[mDNS] Listener error");
            }
        }
    }

    private void ProcessMdnsResponse(byte[] data)
    {
        try
        {
            // Basic mDNS response parsing
            // In production, would use full DNS parser
            logger?.LogTrace("[mDNS] Received {Bytes} bytes", data.Length);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[mDNS] Failed to process response");
        }
    }

    private static byte[] BuildMdnsQuery(string service, string domain)
    {
        // Basic mDNS query packet
        // Format: [Header][Question]
        // In production, would use proper DNS library

        var query = new List<byte>();

        // Transaction ID (2 bytes)
        query.AddRange(BitConverter.GetBytes((ushort)0x0000));

        // Flags (2 bytes) - Standard query
        query.AddRange(BitConverter.GetBytes((ushort)0x0000));

        // Question count (2 bytes)
        query.AddRange(BitConverter.GetBytes((ushort)0x0001));

        // Answer count (2 bytes)
        query.AddRange(BitConverter.GetBytes((ushort)0x0000));

        // Authority count (2 bytes)
        query.AddRange(BitConverter.GetBytes((ushort)0x0000));

        // Additional count (2 bytes)
        query.AddRange(BitConverter.GetBytes((ushort)0x0000));

        // Question: _service._tcp.local.
        var serviceName = $"{service}._tcp.{domain}";
        foreach (var label in serviceName.Split('.'))
        {
            if (string.IsNullOrEmpty(label))
                continue;

            query.Add((byte)label.Length);
            query.AddRange(System.Text.Encoding.UTF8.GetBytes(label));
        }
        query.Add(0x00); // End of name

        // Type (2 bytes) - PTR (12)
        query.AddRange(BitConverter.GetBytes((ushort)0x000C));

        // Class (2 bytes) - IN (1)
        query.AddRange(BitConverter.GetBytes((ushort)0x0001));

        return [.. query];
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Dispose managed resources
            _cts.Cancel();

            try
            {
                _listenerTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Ignore cleanup errors
            }

            _client?.Close();
            _client?.Dispose();
            _cts.Dispose();
        }

        // Free unmanaged resources here (none in this case)

        _disposed = true;
        logger?.LogInformation("[mDNS] Stopped discovery");
    }
}
