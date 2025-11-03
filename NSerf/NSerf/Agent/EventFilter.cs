// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Serf.Events;

namespace NSerf.Agent;

/// <summary>
/// Filters events based on event type and name.
/// Maps to: Go's EventFilter in event_handler.go
/// </summary>
public class EventFilter(string eventType = "*", string name = "")
{
    public string Event { get; set; } = eventType;
    public string Name { get; set; } = name;

    public bool Matches(IEvent evt)
    {
        // Wildcard matches all
        if (Event == "*")
            return true;

        // Match by event type
        var eventTypeName = GetEventTypeName(evt);
        return Event == eventTypeName && MatchesName(evt);
    }

    private bool MatchesName(IEvent evt)
    {
        // No name filter = matches all
        if (string.IsNullOrEmpty(Name))
            return true;

        return evt switch
        {
            // User event name matching
            UserEvent userEvt => userEvt.Name == Name,
            // Query name matching
            Query query => query.Name == Name,
            _ => true
        };
    }

    private static string GetEventTypeName(IEvent evt)
    {
        return evt switch
        {
            MemberEvent { Type: EventType.MemberJoin } => "member-join",
            MemberEvent { Type: EventType.MemberLeave } => "member-leave",
            MemberEvent { Type: EventType.MemberFailed } => "member-failed",
            MemberEvent { Type: EventType.MemberUpdate } => "member-update",
            MemberEvent { Type: EventType.MemberReap } => "member-reap",
            UserEvent => "user",
            Query => "query",
            _ => "unknown"
        };
    }

    public static EventFilter Parse(string eventSpec)
    {
        // Format: "event" or "event:name"
        var parts = eventSpec.Split(':', 2);

        var eventType = parts[0].Trim();
        var name = parts.Length > 1 ? parts[1].Trim() : "";

        // Validate event type
        ValidateEventType(eventType);

        return new EventFilter(eventType, name);
    }

    private static void ValidateEventType(string eventType)
    {
        var validEvents = new[]
        {
            "*", "member-join", "member-leave", "member-failed",
            "member-update", "member-reap", "user", "query"
        };

        if (!validEvents.Contains(eventType))
            throw new ArgumentException($"Invalid event type: {eventType}");
    }
}
