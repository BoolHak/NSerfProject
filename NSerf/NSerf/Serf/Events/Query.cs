// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event.go

using Microsoft.Extensions.Logging;

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

        // Check size limit
        if (raw.Length > SerfInstance.Config.QueryResponseSizeLimit)
        {
            throw new InvalidOperationException(
                $"Response exceeds limit of {SerfInstance.Config.QueryResponseSizeLimit} bytes");
        }

        // Send the response
        await RespondWithMessageAndResponseAsync(raw, resp);
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

        // TODO: Send the response directly to the originator
        // This requires Memberlist.SendToAddress() which will be implemented later
        // For now, just log that we would send
        var addrStr = System.Text.Encoding.UTF8.GetString(Addr);
        SerfInstance.Logger?.LogDebug("[Query] Would send response to {Addr}:{Port}", addrStr, Port);

        // TODO: Relay the response through up to relayFactor other nodes
        // This requires Serf.RelayResponseAsync() which will be implemented in Phase 3
        if (RelayFactor > 0)
        {
            SerfInstance.Logger?.LogDebug("[Query] Would relay response through {RelayFactor} nodes", RelayFactor);
        }
        
        // For now, we'll complete this implementation once the supporting methods exist
        await Task.CompletedTask;

        // Clear the deadline - responses sent
        lock (_respLock)
        {
            Deadline = default(DateTime);
        }
    }
}
