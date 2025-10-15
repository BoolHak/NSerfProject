// Ported from: github.com/hashicorp/memberlist/mock_transport.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using NSerf.Memberlist.Transport;

namespace NSerfTests.Memberlist.Transport;

/// <summary>
/// Factory that produces MockTransport instances which are uniquely addressed
/// and wired up to talk to each other.
/// </summary>
public class MockNetwork
{
    private readonly Dictionary<string, MockTransport> _transportsByAddr = new();
    private readonly Dictionary<string, MockTransport> _transportsByName = new();
    private int _port = 20000;
    
    /// <summary>
    /// Creates a new MockTransport with a unique address, wired up to talk to
    /// other transports in the MockNetwork.
    /// </summary>
    public MockTransport CreateTransport(string name)
    {
        _port++;
        var addr = $"127.0.0.1:{_port}";
        
        var transport = new MockTransport(this, addr, name);
        
        _transportsByAddr[addr] = transport;
        _transportsByName[name] = transport;
        
        return transport;
    }
    
    internal MockTransport? GetTransportByAddr(string addr)
    {
        return _transportsByAddr.GetValueOrDefault(addr);
    }
    
    internal MockTransport? GetTransportByName(string name)
    {
        return _transportsByName.GetValueOrDefault(name);
    }
}

/// <summary>
/// MockTransport directly plumbs messages to other transports in its MockNetwork.
/// </summary>
public class MockTransport : INodeAwareTransport
{
    private readonly MockNetwork _network;
    private readonly string _addr;
    private readonly string _name;
    private readonly Channel<Packet> _packetChannel;
    private readonly Channel<NetworkStream> _streamChannel;
    private bool _disposed;
    
    internal MockTransport(MockNetwork network, string addr, string name)
    {
        _network = network;
        _addr = addr;
        _name = name;
        _packetChannel = Channel.CreateUnbounded<Packet>();
        _streamChannel = Channel.CreateUnbounded<NetworkStream>();
    }
    
    public (IPAddress Ip, int Port) FinalAdvertiseAddr(string ip, int port)
    {
        var parts = _addr.Split(':');
        var address = IPAddress.Parse(parts[0]);
        var addrPort = int.Parse(parts[1]);
        return (address, addrPort);
    }
    
    public async Task<DateTimeOffset> WriteToAsync(byte[] buffer, string addr, CancellationToken cancellationToken = default)
    {
        var address = new Address { Addr = addr, Name = string.Empty };
        return await WriteToAddressAsync(buffer, address, cancellationToken);
    }
    
    public async Task<DateTimeOffset> WriteToAddressAsync(byte[] buffer, Address addr, CancellationToken cancellationToken = default)
    {
        var dest = GetPeer(addr);
        var now = DateTimeOffset.UtcNow;
        
        if (dest == null)
        {
            // UDP behavior: Silently drop packets to non-existent destinations
            // This allows probes to timeout naturally instead of throwing exceptions
            // Real UDP doesn't fail when sending to non-existent addresses
            return now;
        }
        
        var packet = new Packet
        {
            Buf = buffer.ToArray(), // Copy the buffer
            From = new IPEndPoint(IPAddress.Parse(_addr.Split(':')[0]), int.Parse(_addr.Split(':')[1])),
            Timestamp = now
        };
        
        await dest._packetChannel.Writer.WriteAsync(packet, cancellationToken);
        return now;
    }
    
    public ChannelReader<Packet> PacketChannel => _packetChannel.Reader;
    
    public async Task<NetworkStream> DialTimeoutAsync(string addr, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var address = new Address { Addr = addr, Name = string.Empty };
        return await DialAddressTimeoutAsync(address, timeout, cancellationToken);
    }
    
    public async Task<NetworkStream> DialAddressTimeoutAsync(Address addr, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var dest = GetPeer(addr);
        if (dest == null)
        {
            throw new InvalidOperationException($"No route to {addr}");
        }
        
        // Create a pipe for bidirectional communication
        var pipe = new MockStreamPair();
        
        // Send one end to the destination
        await dest._streamChannel.Writer.WriteAsync(pipe.Stream1, cancellationToken);
        
        // Return the other end to the caller
        return pipe.Stream2;
    }
    
    public ChannelReader<NetworkStream> StreamChannel => _streamChannel.Reader;
    
    public Task ShutdownAsync()
    {
        _packetChannel.Writer.Complete();
        _streamChannel.Writer.Complete();
        return Task.CompletedTask;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ShutdownAsync().GetAwaiter().GetResult();
        }
    }
    
    private MockTransport? GetPeer(Address addr)
    {
        if (!string.IsNullOrEmpty(addr.Name))
        {
            return _network.GetTransportByName(addr.Name);
        }
        else
        {
            return _network.GetTransportByAddr(addr.Addr);
        }
    }
}

/// <summary>
/// Creates a pair of connected NetworkStreams for testing.
/// </summary>
internal class MockStreamPair
{
    public NetworkStream Stream1 { get; }
    public NetworkStream Stream2 { get; }
    
    public MockStreamPair()
    {
        // Create two connected sockets
        var socket1 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var socket2 = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        // Bind and connect
        var endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        socket1.Bind(endpoint);
        socket1.Listen(1);
        
        var actualEndpoint = (IPEndPoint)socket1.LocalEndPoint!;
        
        // Connect in background
        var connectTask = Task.Run(() => socket2.Connect(actualEndpoint));
        var acceptedSocket = socket1.Accept();
        connectTask.Wait();
        
        Stream1 = new NetworkStream(acceptedSocket, ownsSocket: true);
        Stream2 = new NetworkStream(socket2, ownsSocket: true);
        
        // Close the listener
        socket1.Close();
    }
}
