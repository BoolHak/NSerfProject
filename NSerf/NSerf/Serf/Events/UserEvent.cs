// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Events;

/// <summary>
/// User-defined event.
/// Minimal implementation for Phase 0 - will be expanded in Phase 3.
/// </summary>
public class UserEvent : Event
{
    /// <summary>
    /// Name of the user event.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Payload of the user event.
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Whether this event can be coalesced.
    /// </summary>
    public bool Coalesce { get; set; }

    /// <summary>
    /// Gets the type of this event.
    /// </summary>
    public EventType EventType() => Events.EventType.User;

    /// <summary>
    /// String representation of this event.
    /// </summary>
    public override string ToString() => $"user-event: {Name}";
}
