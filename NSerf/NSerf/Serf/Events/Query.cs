// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event.go

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
    // Note: Full Respond() functionality requires Serf instance and will be implemented in later phases
    internal uint Id { get; set; }
    internal byte[] Addr { get; set; } = Array.Empty<byte>();
    internal ushort Port { get; set; }
    internal string SourceNodeName { get; set; } = string.Empty;
    internal DateTime Deadline { get; set; }
    internal byte RelayFactor { get; set; }
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

    // Note: Respond() method will be fully implemented when Serf class is complete
    // For now, we have the structure to support testing
}
