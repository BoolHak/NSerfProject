// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

namespace NSerf.Serf;

/// <summary>
/// UserEventData represents a single user event with its name and payload.
/// Used to prevent re-delivery of duplicate events.
/// </summary>
internal class UserEventData
{
    /// <summary>
    /// Name of the user event.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Payload of the user event (arbitrary binary data).
    /// </summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Checks if this event equals another event.
    /// Two events are equal if they have the same name and payload.
    /// </summary>
    public bool Equals(UserEventData? other)
    {
        if (other == null) return false;
        if (Name != other.Name) return false;
        return Payload.SequenceEqual(other.Payload);
    }

    /// <summary>
    /// Returns a string representation of the user event.
    /// </summary>
    public override string ToString()
    {
        return $"UserEvent: {Name} ({Payload.Length} bytes)";
    }
}

/// <summary>
/// UserEventCollection stores all user events at a specific Lamport time.
/// Used for event buffering and de-duplication.
/// </summary>
internal class UserEventCollection
{
    /// <summary>
    /// Lamport time when these events occurred.
    /// </summary>
    public LamportTime LTime { get; set; }

    /// <summary>
    /// List of events at this lamport time.
    /// </summary>
    public List<UserEventData> Events { get; set; } = new();

    /// <summary>
    /// Returns a string representation of the event collection.
    /// </summary>
    public override string ToString()
    {
        return $"UserEvents at LTime {LTime}: {Events.Count} event(s)";
    }
}
