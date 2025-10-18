// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event.go

using Microsoft.Extensions.Logging;
using Address = NSerf.Memberlist.Transport.Address;

namespace NSerf.Serf.Events;

/// <summary>
/// Query is the struct used by EventQuery type events.
/// Represents a distributed query that can be sent to cluster members.
/// </summary>
public class Query : Event
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
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    // Internal fields (matching Go's unexported fields)
    internal uint Id { get; set; }
    internal byte[] Addr { get; set; } = Array.Empty<byte>();
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

        // Create response message
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

        // CRITICAL: Wrap QueryResponse in User message type for memberlist transport
        // (same way Query messages are wrapped when broadcast)
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
            if (Deadline == default(DateTime))
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

        // TODO: Relay the response through up to relayFactor other nodes (Go line 185-188)
        // This requires Serf.RelayResponse() which will be implemented later
        if (RelayFactor > 0)
        {
            SerfInstance.Logger?.LogDebug("[Query] Relaying through {RelayFactor} nodes not yet implemented", RelayFactor);
        }

        // Clear the deadline - responses sent
        lock (_respLock)
        {
            Deadline = default(DateTime);
        }
    }
}
