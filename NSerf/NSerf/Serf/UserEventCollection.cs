// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// UserEventData represents a single user event with its name and payload.
/// Used to prevent re-delivery of duplicate events.
/// </summary>
[MessagePackObject]
public class UserEventData
{
    /// <summary>
    /// Name of the user event.
    /// </summary>
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Payload of the user event (arbitrary binary data).
    /// </summary>
    [Key(1)]
    public byte[] Payload { get; set; } = [];

    /// <summary>
    /// Checks if this event equals another event.
    /// Two events are equal if they have the same name and payload.
    /// </summary>
    public bool Equals(UserEventData? other)
    {
        if (other == null) return false;
        return Name == other.Name && Payload.SequenceEqual(other.Payload);
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
[MessagePackObject]
public class UserEventCollection
{
    /// <summary>
    /// Lamport time when these events occurred.
    /// </summary>
    [Key(0)]
    public LamportTime LTime { get; set; }

    /// <summary>
    /// List of events at this lamport time.
    /// </summary>
    [Key(1)]
    public List<UserEventData> Events { get; set; } = [];

    /// <summary>
    /// Returns a string representation of the event collection.
    /// </summary>
    public override string ToString()
    {
        return $"UserEvents at LTime {LTime}: {Events.Count} event(s)";
    }
}
