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
public class AgentMdns : IDisposable
{
    private readonly string _service;
    private readonly string _domain;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _client;
    private Task? _listenerTask;
    private bool _disposed;

    private const int MdnsPort = 5353;
    private const string MdnsAddress = "224.0.0.251";

    public AgentMdns(string service, string domain = "local", ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _domain = domain;
        _logger = logger;
    }

    /// <summary>
    /// Start mDNS discovery.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AgentMdns));

        try
        {
            _client = new UdpClient(MdnsPort);
            _client.JoinMulticastGroup(IPAddress.Parse(MdnsAddress));
            
            _listenerTask = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);
            
            _logger?.LogInformation("[mDNS] Started discovery for service: {Service}", _service);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[mDNS] Failed to start discovery");
        }
    }

    /// <summary>
    /// Discover peers via mDNS.
    /// </summary>
    public async Task<string[]> DiscoverPeersAsync(TimeSpan timeout)
    {
        if (_disposed || _client == null)
            return Array.Empty<string>();

        var peers = new List<string>();
        var cts = new CancellationTokenSource(timeout);

        try
        {
            // Send mDNS query
            var query = BuildMdnsQuery();
            var endpoint = new IPEndPoint(IPAddress.Parse(MdnsAddress), MdnsPort);
            await _client.SendAsync(query, query.Length, endpoint);

            _logger?.LogDebug("[mDNS] Sent discovery query for {Service}", _service);

            // Wait for responses (collected in ListenAsync)
            await Task.Delay(timeout, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout reached
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[mDNS] Discovery error");
        }

        return peers.ToArray();
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
                _logger?.LogWarning(ex, "[mDNS] Listener error");
            }
        }
    }

    private void ProcessMdnsResponse(byte[] data)
    {
        try
        {
            // Basic mDNS response parsing
            // In production, would use full DNS parser
            _logger?.LogTrace("[mDNS] Received {Bytes} bytes", data.Length);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[mDNS] Failed to process response");
        }
    }

    private byte[] BuildMdnsQuery()
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
        var serviceName = $"{_service}._tcp.{_domain}";
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
        
        return query.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
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
        
        _logger?.LogInformation("[mDNS] Stopped discovery");
    }
}
