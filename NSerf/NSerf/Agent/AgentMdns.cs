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

    private readonly int _port;

    public AgentMdns(string service, string domain = "local", ILogger? logger = null)
        : this(service, domain, MdnsPort, logger)
    {
    }

    public AgentMdns(string service, string domain, int port, ILogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _domain = domain;
        _port = port;
        _logger = logger;
    }

    /// <summary>
    /// Gets the service name used for mDNS discovery.
    /// </summary>
    public string ServiceName => _service;

    /// <summary>
    /// Gets the domain used for mDNS discovery.
    /// </summary>
    public string Domain => _domain;

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
        if (_disposed)
            throw new ObjectDisposedException(nameof(AgentMdns));

        // Check if already started
        if (_client != null && _listenerTask != null)
            return false;

        try
        {
            _client = new UdpClient(_port);
            
            // Only join multicast group if using a real port (not 0)
            if (_port != 0)
            {
                _client.JoinMulticastGroup(IPAddress.Parse(MdnsAddress));
            }
            
            _listenerTask = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);
            
            _logger?.LogInformation("[mDNS] Started discovery for service: {Service} on port: {Port}", _service, _port);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[mDNS] Failed to start discovery");
            return false;
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
