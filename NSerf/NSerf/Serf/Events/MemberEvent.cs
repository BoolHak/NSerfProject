// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Events;

/// <summary>
/// Event related to member changes (join, leave, fail, etc.).
/// Minimal implementation for Phase 0 - will be expanded in Phase 3.
/// </summary>
public class MemberEvent : Event
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
    /// </summary>
    public override string ToString() => Type switch
    {
        Events.EventType.MemberJoin => "member-join",
        Events.EventType.MemberLeave => "member-leave",
        Events.EventType.MemberFailed => "member-failed",
        Events.EventType.MemberUpdate => "member-update",
        Events.EventType.MemberReap => "member-reap",
        _ => "unknown-member-event"
    };
}
