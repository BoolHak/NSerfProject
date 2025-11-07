// Ported from: github.com/hashicorp/memberlist/transport.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace NSerf.Memberlist.Transport;

/// <summary>
/// Packet provides metadata about incoming packets from peers over a packet connection,
/// as well as the packet payload.
/// </summary>
public class Packet
{
    /// <summary>
    /// Raw contents of the packet.
    /// </summary>
    public byte[] Buf { get; set; } = [];

    /// <summary>
    /// Address of the peer that sent the packet.
    /// </summary>
    public EndPoint From { get; set; } = new IPEndPoint(IPAddress.None, 0);

    /// <summary>
    /// Time when the packet was received. Taken as close as possible to actual receipt
    /// time to help make accurate RTT measurements during probes.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Transport abstracts over communicating with other peers.
/// The packet interface is assumed to be best-effort, and the stream interface
/// is assumed to be reliable.
/// </summary>
public interface ITransport : IDisposable
{
    /// <summary>
    /// Gets the final advertisement address given the user's configured values.
    /// Returns the desired IP and port to advertise to the rest of the cluster.
    /// </summary>
    /// <param name="ip">User-configured IP (maybe empty).</param>
    /// <param name="port">User-configured port.</param>
    /// <returns>Tuple of (IP address, port).</returns>
    (IPAddress Ip, int Port) FinalAdvertiseAddr(string ip, int port);

    /// <summary>
    /// Packet-oriented interface that fires off the given payload to the given address
    /// in a connectionless fashion. Returns a timestamp as close as possible to when
    /// the packet was transmitted to help make accurate RTT measurements.
    /// </summary>
    /// <param name="buffer">Data to send.</param>
    /// <param name="addr">Target address in "host:port" format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Timestamp of transmission.</returns>
    Task<DateTimeOffset> WriteToAsync(byte[] buffer, string addr, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a channel that can be read to receive incoming packets from other peers.
    /// </summary>
    ChannelReader<Packet> PacketChannel { get; }

    /// <summary>
    /// Creates a connection that allows two-way communication with a peer.
    /// Generally more expensive than packet connections, used for infrequent operations
    /// like anti-entropy or fallback probes.
    /// </summary>
    /// <param name="addr">Target address in "host:port" format.</param>
    /// <param name="timeout">Connection timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Connected stream.</returns>
    Task<NetworkStream> DialTimeoutAsync(string addr, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a channel that can be read to handle incoming stream connections from other peers.
    /// </summary>
    ChannelReader<NetworkStream> StreamChannel { get; }

    /// <summary>
    /// Called when the memberlist is shutting down to clean up listeners.
    /// </summary>
    Task ShutdownAsync();
}

/// <summary>
/// Address represents a network address with an optional node name.
/// </summary>
public class Address
{
    /// <summary>
    /// Network address as a string, usually in "host:port" format. Required.
    /// </summary>
    public string Addr { get; set; } = string.Empty;

    /// <summary>
    /// The name of the node being addressed. Optional, but some transports may require it.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Returns a string representation of the address.
    /// </summary>
    public override string ToString()
    {
        return !string.IsNullOrEmpty(Name) ? $"{Name} ({Addr})" : Addr;
    }
}

/// <summary>
/// NodeAwareTransport extends Transport with methods that accept Address structures
/// including the node name.
/// </summary>
public interface INodeAwareTransport : ITransport
{
    /// <summary>
    /// Writes to an address including node name information.
    /// </summary>
    Task<DateTimeOffset> WriteToAddressAsync(byte[] buffer, Address addr, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dials with an address including node name information.
    /// </summary>
    Task<NetworkStream> DialAddressTimeoutAsync(Address addr, TimeSpan timeout, CancellationToken cancellationToken = default);
}
