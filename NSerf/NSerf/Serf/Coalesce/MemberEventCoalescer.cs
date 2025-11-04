// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce_member.go

using System.Threading.Channels;
using NSerf.Serf.Events;

namespace NSerf.Serf.Coalesce;

/// <summary>
/// Internal structure to track coalesced events for a specific member.
/// </summary>
internal class CoalesceEvent
{
    public EventType Type { get; set; }
    public Member? Member { get; set; }
}

/// <summary>
/// MemberEventCoalescer coalesces member-related events (Join, Leave, Failed, Update, Reap).
/// </summary>
internal class MemberEventCoalescer : ICoalescer
{
    private readonly Dictionary<string, EventType> _lastEvents = [];
    private readonly Dictionary<string, CoalesceEvent> _latestEvents = [];

    public bool Handle(IEvent e)
    {
        return e.EventType() switch
        {
            EventType.MemberJoin => true,
            EventType.MemberLeave => true,
            EventType.MemberFailed => true,
            EventType.MemberUpdate => true,
            EventType.MemberReap => true,
            _ => false
        };
    }

    public void Coalesce(IEvent rawEvent)
    {
        var e = (MemberEvent)rawEvent;
        foreach (var m in e.Members)
        {
            _latestEvents[m.Name] = new CoalesceEvent
            {
                Type = e.Type,
                Member = m
            };
        }
    }

    public void Flush(ChannelWriter<IEvent> outChan)
    {
        // Coalesce the various events we got into a single set of events.
        var events = new Dictionary<EventType, MemberEvent>();

        foreach (var (name, cevent) in _latestEvents)
        {
            // Check if we sent the same event before
            var hasPrevious = _lastEvents.TryGetValue(name, out var previous);

            // If we sent the same event before, then ignore
            // unless it is a MemberUpdate
            if (hasPrevious && previous == cevent.Type && cevent.Type != EventType.MemberUpdate)
            {
                continue;
            }

            // Update our last event
            _lastEvents[name] = cevent.Type;

            // Add it to our event
            if (!events.TryGetValue(cevent.Type, out var memberEvent))
            {
                memberEvent = new MemberEvent { Type = cevent.Type };
                events[cevent.Type] = memberEvent;
            }

            if (cevent.Member != null)
            {
                memberEvent.Members.Add(cevent.Member);
            }
        }

        // Send out those events
        foreach (var evt in events.Values)
        {
            outChan.TryWrite(evt);
        }

        // Clear for the next cycle (not needed in Go because of garbage collection)
        _latestEvents.Clear();
    }
}
