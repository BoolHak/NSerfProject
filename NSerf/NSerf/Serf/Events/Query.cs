// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Events;

/// <summary>
/// Query event for distributed queries.
/// Minimal implementation for Phase 0 - will be expanded in Phase 3.
/// </summary>
public class Query : Event
{
    /// <summary>
    /// Name of the query.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Payload of the query.
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Gets the type of this event.
    /// </summary>
    public EventType EventType() => Events.EventType.Query;

    /// <summary>
    /// String representation of this event.
    /// </summary>
    public override string ToString() => $"query: {Name}";
}
