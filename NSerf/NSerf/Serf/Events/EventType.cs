// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event.go

namespace NSerf.Serf.Events;

/// <summary>
/// Types of events that can occur in Serf.
/// Values match the Go implementation order.
/// </summary>
public enum EventType
{
    /// <summary>
    /// Member joined the cluster.
    /// </summary>
    MemberJoin = 0,

    /// <summary>
    /// Member left the cluster.
    /// </summary>
    MemberLeave = 1,

    /// <summary>
    /// Member failed.
    /// </summary>
    MemberFailed = 2,

    /// <summary>
    /// Member was updated.
    /// </summary>
    MemberUpdate = 3,

    /// <summary>
    /// Member was reaped.
    /// </summary>
    MemberReap = 4,

    /// <summary>
    /// User-defined event.
    /// </summary>
    User = 5,

    /// <summary>
    /// Query event.
    /// </summary>
    Query = 6
}

/// <summary>
/// Extension methods for EventType.
/// </summary>
public static class EventTypeExtensions
{
    /// <summary>
    /// Returns a string representation of the event type.
    /// Throws InvalidOperationException for unknown types (matches Go's panic behavior).
    /// </summary>
    public static string String(this EventType eventType)
    {
        return eventType switch
        {
            EventType.MemberJoin => "member-join",
            EventType.MemberLeave => "member-leave",
            EventType.MemberFailed => "member-failed",
            EventType.MemberUpdate => "member-update",
            EventType.MemberReap => "member-reap",
            EventType.User => "user",
            EventType.Query => "query",
            _ => throw new InvalidOperationException($"unknown event type: {(int)eventType}")
        };
    }
}
