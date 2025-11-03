// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/event_delegate.go

using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Serf;

/// <summary>
/// EventDelegate is the Serf implementation of IEventDelegate from Memberlist.
/// It bridges Memberlist node events (join, leave, update) to Serf's internal handlers.
/// </summary>
/// <remarks>
/// Creates a new EventDelegate for the given Serf instance.
/// </remarks>
/// <param name="serf">The Serf instance to forward events to</param>
/// <exception cref="ArgumentNullException">Thrown if serf is null</exception>
internal class EventDelegate(Serf serf) : IEventDelegate
{
    private readonly Serf _serf = serf ?? throw new ArgumentNullException(nameof(serf));

    /// <summary>
    /// Invoked when a node is detected to have joined the cluster.
    /// Forwards to Serf's internal node join handler.
    /// </summary>
    /// <param name="node">The node that joined</param>
    public void NotifyJoin(Node node)
    {
        _serf.HandleNodeJoin(node);
    }

    /// <summary>
    /// Invoked when a node is detected to have left the cluster.
    /// Forwards to Serf's internal node leave handler.
    /// </summary>
    /// <param name="node">The node that left</param>
    public void NotifyLeave(Node node)
    {
        _serf.HandleNodeLeave(node);
    }

    /// <summary>
    /// Invoked when a node is detected to have updated (usually metadata).
    /// Forwards to Serf's internal node update handler.
    /// </summary>
    /// <param name="node">The node that was updated</param>
    public void NotifyUpdate(Node node)
    {
        _serf.HandleNodeUpdate(node);
    }
}
