// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/coalesce.go

using System.Threading.Channels;
using NSerf.Serf.Events;

namespace NSerf.Serf.Coalesce;

/// <summary>
/// ICoalescer is a simple interface that must be implemented to be used inside of a coalesceLoop.
/// </summary>
internal interface ICoalescer
{
    /// <summary>
    /// Can the coalescer handle this event? If not, it is directly passed through to the destination channel.
    /// </summary>
    bool Handle(Event e);

    /// <summary>
    /// Invoked to coalesce the given event.
    /// </summary>
    void Coalesce(Event e);

    /// <summary>
    /// Invoked to flush the coalesced events.
    /// </summary>
    void Flush(ChannelWriter<Event> outChan);
}
