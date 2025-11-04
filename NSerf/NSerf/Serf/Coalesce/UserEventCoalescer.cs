// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce_user.go

using System.Threading.Channels;
using NSerf.Serf.Events;

namespace NSerf.Serf.Coalesce;

/// <summary>
/// Tracks the latest user events at a specific lamport time.
/// </summary>
internal class LatestUserEvents
{
    public LamportTime LTime { get; set; }
    public List<IEvent> Events { get; set; } = [];
}

/// <summary>
/// UserEventCoalescer coalesces user-defined events based on their name and lamport time.
/// Only handles events where Coalesce is true.
/// </summary>
internal class UserEventCoalescer : ICoalescer
{
    // Maps an event name into the latest versions
    private readonly Dictionary<string, LatestUserEvents> _events = [];

    public bool Handle(IEvent e)
    {
        // Only handle EventUser messages
        if (e.EventType() != EventType.User)
        {
            return false;
        }

        // Check if coalescing is enabled
        var user = (UserEvent)e;
        return user.Coalesce;
    }

    public void Coalesce(IEvent e)
    {
        var user = (UserEvent)e;

        // Check if we have existing events for this name
        if (!_events.TryGetValue(user.Name, out var latest))
        {
            // Create a new entry
            latest = new LatestUserEvents
            {
                LTime = user.LTime,
                Events = [e]
            };
            _events[user.Name] = latest;
            return;
        }

        // If this message has a newer LTime, replace the old ones
        if (user.LTime > latest.LTime)
        {
            latest.LTime = user.LTime;
            latest.Events = [e];
            return;
        }

        // If the same age, save it
        if (latest.LTime == user.LTime)
        {
            latest.Events.Add(e);
        }
        // If older LTime, ignore it (implicit in Go code)
    }

    public void Flush(ChannelWriter<IEvent> outChan)
    {
        foreach (var e in _events.Values.SelectMany(latest => latest.Events))
        {
            outChan.TryWrite(e);
        }

        // Clear for the next cycle
        _events.Clear();
    }
}
