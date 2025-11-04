// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event.go

using Microsoft.Extensions.Logging;
using System.Net;
using System.Linq;
using Address = NSerf.Memberlist.Transport.Address;

namespace NSerf.Serf.Events;

/// <summary>
/// Query is the struct used by EventQuery type events.
/// Represents a distributed query that can be sent to cluster members.
/// </summary>
public class Query : IEvent
{
    /// <summary>
    /// Lamport time when the query was created.
    /// </summary>
    public LamportTime LTime { get; set; }

    /// <summary>
    /// Name of the query.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Payload of the query.
    /// </summary>
    public byte[] Payload { get; set; } = [];

    // Internal fields (matching Go's unexported fields)
    internal uint Id { get; set; }
    internal byte[] Addr { get; set; } = [];
    internal ushort Port { get; set; }
    internal string SourceNodeName { get; set; } = string.Empty;
    internal DateTime Deadline { get; set; }
    internal byte RelayFactor { get; set; }
    internal Serf? SerfInstance { get; set; }
    private readonly object _respLock = new();

    /// <summary>
    /// Gets the type of this event.
    /// </summary>
    public EventType EventType() => Events.EventType.Query;

    /// <summary>
    /// String representation of this event.
    /// </summary>
    public override string ToString() => $"query: {Name}";

    /// <summary>
    /// SourceNode returns the name of the node initiating the query.
    /// </summary>
    public string SourceNode() => SourceNodeName;

    /// <summary>
    /// Deadline returns the time by which a response must be sent.
    /// </summary>
    public DateTime GetDeadline() => Deadline;

    /// <summary>
    /// Respond sends a response to the user query.
    /// Maps to: Go's Respond() method
    /// </summary>
    public async Task RespondAsync(byte[] payload)
    {
        if (SerfInstance == null)
        {
            throw new InvalidOperationException("Cannot respond to query without Serf instance");
        }

        // Create a response message
        var resp = new MessageQueryResponse
        {
            LTime = LTime,
            ID = Id,
            From = SerfInstance.Config.NodeName,
            Payload = payload
        };

        // Encode the response
        var raw = SerfInstance.EncodeMessage(MessageType.QueryResponse, resp);

        // Check size limit (before wrapping)
        if (raw.Length > SerfInstance.Config.QueryResponseSizeLimit)
        {
            throw new InvalidOperationException(
                $"Response exceeds limit of {SerfInstance.Config.QueryResponseSizeLimit} bytes");
        }

        // CRITICAL: Wrap QueryResponse in the User message type for memberlist transport
        // (the same way Query messages are wrapped when broadcast)
        var wrapped = new byte[1 + raw.Length];
        wrapped[0] = (byte)NSerf.Memberlist.Messages.MessageType.User;
        Array.Copy(raw, 0, wrapped, 1, raw.Length);

        // Send the response
        await RespondWithMessageAndResponseAsync(wrapped, resp);
    }

    /// <summary>
    /// Sends a query response directly to the originator and relays it.
    /// Maps to: Go's respondWithMessageAndResponse()
    /// </summary>
    private async Task RespondWithMessageAndResponseAsync(byte[] raw, MessageQueryResponse resp)
    {
        if (SerfInstance == null)
        {
            throw new InvalidOperationException("Cannot respond without Serf instance");
        }

        lock (_respLock)
        {
            // Check if we've already responded
            if (Deadline == default)
            {
                throw new InvalidOperationException("Response already sent");
            }

            // Ensure we aren't past our response deadline
            if (DateTime.UtcNow > Deadline)
            {
                throw new InvalidOperationException("Response is past the deadline");
            }
        }

        // Send the response directly to the originator (matching Go implementation line 174-183)
        var addrStr = System.Text.Encoding.UTF8.GetString(Addr);
        var targetAddr = new Address
        {
            Addr = $"{addrStr}:{Port}",
            Name = SourceNodeName ?? string.Empty
        };

        try
        {
            await SerfInstance.Memberlist!.SendToAddress(targetAddr, raw, CancellationToken.None);
            SerfInstance.Logger?.LogDebug("[Query] Sent response to {Addr}", targetAddr.Addr);
        }
        catch (Exception ex)
        {
            SerfInstance.Logger?.LogError(ex, "[Query] Failed to send response to {Addr}", targetAddr.Addr);
            throw;
        }

        // Relay the response through up to relayFactor other nodes (Go line 185-188)
        if (RelayFactor > 0)
        {

            try
            {
                // Destination for the relayed message is the original requester
                var destAddrStr = System.Text.Encoding.UTF8.GetString(Addr);
                var destIp = IPAddress.Parse(destAddrStr);
                var destEp = new IPEndPoint(destIp, Port);

                // Encode relay message payload (inner: [Relay][header][QueryResponseType][resp])
                var relayPayload = MessageCodec.EncodeRelayMessage(MessageType.QueryResponse, destEp, SourceNodeName ?? string.Empty, resp);

                // Wrap as Memberlist.User for transport layer
                var relayPacket = new byte[1 + relayPayload.Length];
                relayPacket[0] = (byte)NSerf.Memberlist.Messages.MessageType.User;
                Array.Copy(relayPayload, 0, relayPacket, 1, relayPayload.Length);

                // Choose up to RelayFactor live peers excluding the local node
                var localName = SerfInstance.Config.NodeName;
                var candidates = SerfInstance.Members()
                    .Where(m => m.Status == MemberStatus.Alive && m.Name != localName)
                    .ToList();

                if (candidates.Count > 0)
                {
                    var selected = QueryHelpers.KRandomMembers(Math.Min((int)RelayFactor, candidates.Count), candidates);

                    foreach (var m in selected)
                    {
                        var relayAddr = new Address
                        {
                            Addr = $"{m.Addr}:{m.Port}",
                            Name = m.Name
                        };

                        // Fire-and-forget relay send
                        _ = SerfInstance.Memberlist!.SendToAddress(relayAddr, relayPacket, CancellationToken.None);
                        SerfInstance.Logger?.LogDebug("[Query] Relayed response via peer {Peer} to {Dest}", m.Name, destEp);
                    }
                }
            }
            catch (Exception rex)
            {
                SerfInstance.Logger?.LogWarning(rex, "[Query] Relay forwarding encountered an error");
            }
        }

        // Clear the deadline - responses sent
        lock (_respLock)
        {
            Deadline = default;
        }
    }
}
