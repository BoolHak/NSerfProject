// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Phase 9.2: Event delegate that connects Memberlist to Serf

using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Serf;

/// <summary>
/// Event delegate that routes memberlist events to Serf's event handling logic.
/// Implements IEventDelegate to receive notifications from the memberlist.
/// </summary>
internal class SerfEventDelegate(Serf serf) : IEventDelegate
{
    private readonly Serf _serf = serf ?? throw new ArgumentNullException(nameof(serf));

    /// <summary>
    /// Called by memberlist when a node joins the cluster.
    /// </summary>
    public void NotifyJoin(Node node)
    {
        _serf.HandleNodeJoin(node);
    }

    /// <summary>
    /// Called by memberlist when a node leaves the cluster.
    /// </summary>
    public void NotifyLeave(Node node)
    {
        // Check if this is a graceful leave (node == from in the Dead message)
        // This is indicated by the node's state being Left (not Dead) in memberlist
        _serf.HandleNodeLeave(node);
    }

    /// <summary>
    /// Called by memberlist when a node is updated (usually metadata/tags).
    /// </summary>
    public void NotifyUpdate(Node node)
    {
        _serf.HandleNodeUpdate(node);
    }
}
