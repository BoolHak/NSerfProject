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
    private readonly IEventDelegate? _eventDelegate = eventDelegate;
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Notifies about a node joining.
    /// </summary>
    public void NotifyJoin(Node node)
    {
        try
        {
            _eventDelegate?.NotifyJoin(node);
            _logger?.LogInformation("Node joined: {Node}", node.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error notifying join for {Node}", node.Name);
        }
    }

    /// <summary>
    /// Notifies about a node leaving.
    /// </summary>
    public void NotifyLeave(Node node)
    {
        try
        {
            _eventDelegate?.NotifyLeave(node);
            _logger?.LogInformation("Node left: {Node}", node.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error notifying leave for {Node}", node.Name);
        }
    }

    /// <summary>
    /// Notifies about a node update.
    /// </summary>
    public void NotifyUpdate(Node node)
    {
        try
        {
            _eventDelegate?.NotifyUpdate(node);
            _logger?.LogDebug("Node updated: {Node}", node.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error notifying update for {Node}", node.Name);
        }
    }
}
