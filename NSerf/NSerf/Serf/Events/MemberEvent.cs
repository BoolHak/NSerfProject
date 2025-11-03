// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event.go

namespace NSerf.Serf.Events;

/// <summary>
/// MemberEvent is the struct used for member related events.
/// Because Serf coalesces events, an event may contain multiple members.
/// </summary>
public class MemberEvent : IEvent
{
    /// <summary>
    /// Type of member event.
    /// </summary>
    public EventType Type { get; set; }

    /// <summary>
    /// Members involved in this event.
    /// </summary>
    public List<Member> Members { get; set; } = new();

    /// <summary>
    /// Gets the type of this event.
    /// </summary>
    public EventType EventType() => Type;

    /// <summary>
    /// String representation of this event.
    /// Throws InvalidOperationException for invalid event types (matches Go's panic behavior).
    /// </summary>
    public override string ToString() => Type switch
    {
        Events.EventType.MemberJoin => "member-join",
        Events.EventType.MemberLeave => "member-leave",
        Events.EventType.MemberFailed => "member-failed",
        Events.EventType.MemberUpdate => "member-update",
        Events.EventType.MemberReap => "member-reap",
        _ => throw new InvalidOperationException($"unknown event type: {(int)Type}")
    };
}
