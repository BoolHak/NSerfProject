// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/ping_delegate.go

using MessagePack;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Serf;

/// <summary>
/// PingDelegate is the Serf implementation of IPingDelegate from Memberlist.
/// It handles ping/ack messages to update network coordinates and track RTT.
/// </summary>
internal class PingDelegate : IPingDelegate
{
    private readonly Serf _serf;

    /// <summary>
    /// Internal version for the ping message, above the normal versioning from protocol version.
    /// Enables small updates to the ping message without a full protocol bump.
    /// </summary>
    internal const byte PingVersion = 1;

    /// <summary>
    /// Creates a new PingDelegate for the given Serf instance.
    /// </summary>
    /// <param name="serf">The Serf instance to coordinate with</param>
    /// <exception cref="ArgumentNullException">Thrown if serf is null</exception>
    public PingDelegate(Serf serf)
    {
        _serf = serf ?? throw new ArgumentNullException(nameof(serf));
    }

    /// <summary>
    /// Called to produce a payload to send back in response to a ping request.
    /// Returns the node's current coordinate for network coordinate system.
    /// </summary>
    /// <returns>Payload containing version byte + serialized coordinate</returns>
    public byte[] AckPayload()
    {
        // If coordinates are disabled, return empty payload
        if (_serf.Config.DisableCoordinates)
        {
            return Array.Empty<byte>();
        }

        try
        {
            // Get current coordinate from coordinate client
            var coordinate = _serf.GetCoordinate();
            
            // Serialize: [version byte][msgpack coordinate]
            var coordinateBytes = MessagePackSerializer.Serialize(coordinate);
            var payload = new byte[1 + coordinateBytes.Length];
            payload[0] = PingVersion;
            Array.Copy(coordinateBytes, 0, payload, 1, coordinateBytes.Length);
            
            return payload;
        }
        catch (Exception ex)
        {
            _serf.Logger?.LogError(ex, "[Serf] Failed to encode coordinate for ack payload");
            return new byte[] { PingVersion }; // Return just version byte on error
        }
    }

    /// <summary>
    /// Called when this node successfully completes a direct ping of a peer node.
    /// Updates the network coordinate based on the RTT and received coordinate.
    /// </summary>
    /// <param name="other">The node that responded</param>
    /// <param name="rtt">Round-trip time for the ping</param>
    /// <param name="payload">Payload received in the ack (contains coordinate)</param>
    public void NotifyPingComplete(Node other, TimeSpan rtt, ReadOnlySpan<byte> payload)
    {
        // Early return if no payload
        if (payload.IsEmpty)
        {
            return;
        }

        // Verify ping version in the header
        var version = payload[0];
        if (version != PingVersion)
        {
            _serf.Logger?.LogWarning("[Serf] Unsupported ping version: {Version}", version);
            return;
        }

        // If coordinates are disabled, nothing more to do
        if (_serf.Config.DisableCoordinates)
        {
            return;
        }

        try
        {
            // Process the remainder of the message as a coordinate
            var coordinateBytes = payload[1..];
            if (coordinateBytes.IsEmpty)
            {
                return;
            }

            var remoteCoordinate = MessagePackSerializer.Deserialize<Coordinate.Coordinate>(coordinateBytes.ToArray());

            // Update our coordinate based on the ping
            _serf.UpdateCoordinate(other.Name, remoteCoordinate, rtt);
        }
        catch (Exception ex)
        {
            _serf.Logger?.LogError(ex, "[Serf] Failed to decode/update coordinate from ping");
        }
    }
}
