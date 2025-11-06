// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Notifies delegates about node events.
/// </summary>
public class EventNotifier(IEventDelegate? eventDelegate = null, ILogger? logger = null)
{
    /// <summary>
    /// Notifies about a node joining.
    /// </summary>
    public void NotifyJoin(Node node)
    {
        try
        {
            eventDelegate?.NotifyJoin(node);
            logger?.LogInformation("Node joined: {Node}", node.Name);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error notifying join for {Node}", node.Name);
        }
    }

    /// <summary>
    /// Notifies about a node leaving.
    /// </summary>
    public void NotifyLeave(Node node)
    {
        try
        {
            eventDelegate?.NotifyLeave(node);
            logger?.LogInformation("Node left: {Node}", node.Name);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error notifying leave for {Node}", node.Name);
        }
    }

    /// <summary>
    /// Notifies about a node update.
    /// </summary>
    public void NotifyUpdate(Node node)
    {
        try
        {
            eventDelegate?.NotifyUpdate(node);
            logger?.LogDebug("Node updated: {Node}", node.Name);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error notifying update for {Node}", node.Name);
        }
    }
}
