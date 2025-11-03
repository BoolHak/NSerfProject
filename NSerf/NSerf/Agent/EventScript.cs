// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Agent;

/// <summary>
/// Represents an event handler script with its filter.
/// Maps to: Go's EventScript in event_handler.go
/// </summary>
public class EventScript
{
    public EventFilter Filter { get; } = null!;
    public string Script { get; } = string.Empty;

    public EventScript()
    {
    }

    public EventScript(EventFilter filter, string script)
    {
        Filter = filter;
        Script = script;
    }

    /// <summary>
    /// Parses event handler specification.
    /// Format: "script.sh" or "event=script.sh" or "event:name=script.sh"
    /// Multiple events: "member-leave,member-failed=script.sh"
    /// </summary>
    public static List<EventScript> Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException("Event script specification cannot be empty");

        // Split on '=' to get events and script
        var parts = spec.Split('=', 2);

        if (parts.Length == 1)
        {
            // No filter, matches all events: "script.sh"
            return
            [
                new EventScript(new EventFilter(), parts[0].Trim())
            ];
        }

        var eventsPart = parts[0].Trim();
        var script = parts[1].Trim();

        // Split comma-separated events: "member-leave,member-failed"
        var events = eventsPart.Split(',', StringSplitOptions.RemoveEmptyEntries);

        return events.Select(eventSpec => EventFilter.Parse(eventSpec.Trim()))
            .Select(filter => new EventScript(filter, script)).ToList();
    }
}
