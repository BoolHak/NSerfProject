// Ported from: github.com/hashicorp/memberlist/net_transport.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;

namespace NSerf.Memberlist.Transport;

/// <summary>
/// NetTransport is a Transport implementation that uses UDP for packet operations
/// and TCP connections for stream operations.
/// </summary>
public class NetTransport : INodeAwareTransport
{
    private const int UdpPacketBufSize = 65536;
    private const int UdpRecvBufSize = 2 * 1024 * 1024;

    private readonly NetTransportConfig _config;
    private readonly Channel<Packet> _packetChannel;
    private readonly Channel<NetworkStream> _streamChannel;
    private readonly ILogger? _logger;
    private readonly List<TcpListener> _tcpListeners = [];
    private readonly List<UdpClient> _udpListeners = [];
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly List<Task> _backgroundTasks = [];
    private int _shutdown;
    private bool _disposed;

    // Static lock and port counter for thread-safe port allocation across instances
    private static readonly object PortLock = new();
    private static int _nextPort = 20000; // Start above Windows reserved ranges

    private NetTransport(NetTransportConfig config)
    {
        _config = config;
        _logger = config.Logger;
        _packetChannel = Channel.CreateUnbounded<Packet>();
        _streamChannel = Channel.CreateUnbounded<NetworkStream>();
    }

    /// <summary>
    /// Creates a new NetTransport with the given configuration.
    /// All network listeners will be created and listening.
    /// </summary>
    public static NetTransport Create(NetTransportConfig config)
    {
        if (config.BindAddrs.Count == 0)
            throw new ArgumentException("At least one bind address is required", nameof(config));

        var transport = new NetTransport(config);

        try
        {
            transport.Initialize();
            return transport;
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    private void Initialize()
    {
        var port = _config.BindPort;

        // On Windows, if port is 0, try to get a port from safe range to avoid reserved ports
        if (port == 0 && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            port = GetAvailablePortOnWindows();
            _logger?.LogDebug("[NetTransport] Windows: Selected port {Port} from safe range", port);
        }

        // Build all TCP and UDP listeners
        foreach (var addr in _config.BindAddrs)
        {
            var ip = IPAddress.Parse(addr);

            // Create TCP listener with SO_REUSEADDR to avoid TIME_WAIT issues
            var tcpListener = new TcpListener(ip, port);
            // On Windows, disable ExclusiveAddressUse to allow port reuse
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
                tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
            tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            tcpListener.Start();
            _tcpListeners.Add(tcpListener);

            // If port was still 0 (non-Windows), use the OS-assigned port for all listeners
            if (port == 0)
            {
                port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                _logger?.LogDebug("[NetTransport] OS-assigned port: {Port}", port);
            }

            // Create UDP listener with SO_REUSEADDR to avoid TIME_WAIT issues
            var udpListener = new UdpClient();
            // On Windows, disable ExclusiveAddressUse to allow port reuse
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, false);
            }
            udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpListener.Client.Bind(new IPEndPoint(ip, port));

            // Try to set large receive buffer
            try
            {
                udpListener.Client.ReceiveBufferSize = UdpRecvBufSize;
            }
            catch
            {
                // Fall back to smaller buffer if needed
                try
                {
                    udpListener.Client.ReceiveBufferSize = UdpRecvBufSize / 2;
                }
                catch
                {
                    // Use default if we can't set it
                }
            }

            _udpListeners.Add(udpListener);
        }

        // Start background listeners
        for (var i = 0; i < _config.BindAddrs.Count; i++)
        {
            var tcpListener = _tcpListeners[i];
            var udpListener = _udpListeners[i];

            _backgroundTasks.Add(Task.Run(() => TcpListenAsync(tcpListener)));
            _backgroundTasks.Add(Task.Run(() => UdpListenAsync(udpListener)));
        }
    }

    /// <summary>
    /// Gets an available port on Windows from a safe range (20000-30000)
    /// to avoid Windows reserved port ranges caused by Hyper-V/Docker/WSL2.
    /// Thread-safe for parallel test execution.
    /// Reference: https://github.com/dotnet/runtime/issues/28667
    /// </summary>
    private int GetAvailablePortOnWindows()
    {
        lock (PortLock)
        {
            const int maxAttempts = 100;
            const int minPort = 20000; // Above Windows reserved ranges
            const int maxPort = 30000; // Stay within safe range

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var port = _nextPort++;

                // Wrap around if we exceed max port
                if (_nextPort > maxPort) _nextPort = minPort;

                // Try to bind a test socket to verify port is available
                try
                {
                    using var testSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    testSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    testSocket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                    _logger?.LogTrace("[NetTransport] Windows: Port {Port} is available", port);
                    return port;
                }
                catch (SocketException ex)
                {
                    // Port not available or in Windows reserved range, try next
                    _logger?.LogTrace("[NetTransport] Windows: Port {Port} not available: {Error}", port, ex.Message);
                    continue;
                }
            }

            // If we exhausted attempts, fall back to OS assignment (port 0)
            // This will let the OS pick but may still hit reserved ranges
            _logger?.LogWarning("[NetTransport] Windows: Could not find available port in safe range after {Attempts} attempts, falling back to OS assignment", maxAttempts);
            return 0;
        }
    }

    /// <summary>
    /// Gets the auto-assigned port if BindPort was 0.
    /// </summary>
    public int GetAutoBindPort()
    {
        return ((IPEndPoint)_tcpListeners[0].LocalEndpoint).Port;
    }

    public (IPAddress Ip, int Port) FinalAdvertiseAddr(string ip, int port)
    {
        IPAddress advertiseAddr;
        int advertisePort;

        if (!string.IsNullOrEmpty(ip))
        {
            advertiseAddr = IPAddress.Parse(ip);

            // Convert to IPv4 if possible
            if (advertiseAddr.IsIPv4MappedToIPv6) advertiseAddr = advertiseAddr.MapToIPv4();

            advertisePort = port;
        }
        else
        {
            // Use bound address
            var endpoint = (IPEndPoint)_tcpListeners[0].LocalEndpoint;
            advertiseAddr = endpoint.Address;

            // If bound to 0.0.0.0, try to get a specific interface address
            if (advertiseAddr.Equals(IPAddress.Any)) advertiseAddr = GetPrivateIp();

            advertisePort = GetAutoBindPort();
        }

        return (advertiseAddr, advertisePort);
    }

    public async Task<DateTimeOffset> WriteToAsync(byte[] buffer, string addr, CancellationToken cancellationToken = default)
    {
        var address = new Address { Addr = addr, Name = string.Empty };
        return await WriteToAddressAsync(buffer, address, cancellationToken);
    }

    public async Task<DateTimeOffset> WriteToAddressAsync(byte[] buffer, Address addr, CancellationToken cancellationToken = default)
    {
        var parts = addr.Addr.Split(':');
        var host = string.Join(':', parts.Take(parts.Length - 1));
        var port = int.Parse(parts[^1]);

        var endpoint = new IPEndPoint(IPAddress.Parse(host), port);

        // Use the first UDP listener to send
        await _udpListeners[0].SendAsync(buffer, endpoint, cancellationToken);

        return DateTimeOffset.UtcNow;
    }

    public ChannelReader<Packet> PacketChannel => _packetChannel.Reader;
    public ChannelReader<NetworkStream> StreamChannel => _streamChannel.Reader;

    public async Task<NetworkStream> DialTimeoutAsync(string addr, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var address = new Address { Addr = addr, Name = string.Empty };
        return await DialAddressTimeoutAsync(address, timeout, cancellationToken);
    }
    public async Task<NetworkStream> DialAddressTimeoutAsync(Address addr, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var parts = addr.Addr.Split(':');
        var host = string.Join(':', parts.Take(parts.Length - 1));
        var port = int.Parse(parts[^1]);

        var client = new TcpClient();
        await client.ConnectAsync(host, port, cts.Token);

        return new NetworkStream(client.Client, ownsSocket: true);
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.CompareExchange(ref _shutdown, 1, 0) == 1)
            return;

        _logger?.LogInformation("NetTransport: Shutting down");

        // Signal shutdown
        await _shutdownCts.CancelAsync();

        // Close all listeners
        foreach (var listener in _tcpListeners) listener.Stop();
        foreach (var listener in _udpListeners) listener.Close();

        // Wait for background tasks
        try
        {
            await Task.WhenAll(_backgroundTasks);
        }
        catch
        {
            // Expected during shutdown
        }

        // Complete channels
        _packetChannel.Writer.Complete();
        _streamChannel.Writer.Complete();
    }

    private async Task TcpListenAsync(TcpListener listener)
    {
        const int baseDelayMs = 5;
        const int maxDelayMs = 1000;
        var delayMs = 0;

        while (!_shutdownCts.Token.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(_shutdownCts.Token);
                delayMs = 0; // Reset delay on success

                var stream = client.GetStream();
                await _streamChannel.Writer.WriteAsync(stream, _shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref _shutdown, 0, 0) == 1)
                    break;

                _logger?.LogError(ex, "Error accepting TCP connection");

                // Exponential backoff
                delayMs = delayMs == 0 ? baseDelayMs : Math.Min(delayMs * 2, maxDelayMs);
                await Task.Delay(delayMs, _shutdownCts.Token);
            }
        }
    }

    private async Task UdpListenAsync(UdpClient listener)
    {
        while (!_shutdownCts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(_shutdownCts.Token);
                var timestamp = DateTimeOffset.UtcNow;

                // Validate packet
                if (result.Buffer.Length < 1)
                {
                    _logger?.LogWarning("UDP packet too short ({Length} bytes)", result.Buffer.Length);
                    continue;
                }

                var packet = new Packet
                {
                    Buf = result.Buffer,
                    From = result.RemoteEndPoint,
                    Timestamp = timestamp
                };

                await _packetChannel.Writer.WriteAsync(packet, _shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref _shutdown, 0, 0) == 1)
                {
                    break;
                }

                _logger?.LogError(ex, "Error reading UDP packet");
            }
        }
    }

    private static IPAddress GetPrivateIp()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            IPAddress? loopbackFallback = null;

            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                // Check if it's a private IP
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168))
                {
                    return ip;
                }

                // Save loopback address as fallback (127.x.x.x)
                if (bytes[0] == 127)
                {
                    loopbackFallback = ip;
                }
            }

            // If no private IP found, return loopback as fallback
            if (loopbackFallback != null)
            {
                return loopbackFallback;
            }
        }
        catch
        {
            // Ignore errors
        }

        // Last resort: return 127.0.0.1
        return IPAddress.Loopback;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ShutdownAsync().GetAwaiter().GetResult();
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
