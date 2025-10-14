// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Handles push/pull full state synchronization.
/// </summary>
public class PushPullSynchronizer
{
    private readonly ILogger? _logger;
    private readonly NodeLifecycleManager _nodeManager;
    
    public PushPullSynchronizer(NodeLifecycleManager nodeManager, ILogger? logger = null)
    {
        _nodeManager = nodeManager;
        _logger = logger;
    }
    
    /// <summary>
    /// Initiates a push/pull sync with a random node.
    /// </summary>
    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        var nodes = _nodeManager.GetAliveNodes();
        if (nodes.Count == 0)
        {
            return false;
        }
        
        var random = new Random();
        var target = nodes[random.Next(nodes.Count)];
        
        _logger?.LogDebug("Starting push/pull sync with {Node}", target.Name);
        
        try
        {
            // TODO: Send push/pull request
            await Task.Delay(10, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Push/pull sync failed with {Node}", target.Name);
            return false;
        }
    }
    
    /// <summary>
    /// Handles incoming push/pull request.
    /// </summary>
    public Task<List<NodeState>> HandlePushPullRequestAsync(
        List<NodeState> remoteNodes,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Handling push/pull request with {Count} remote nodes", remoteNodes.Count);
        
        // Merge remote state
        foreach (var remoteNode in remoteNodes)
        {
            var localNode = _nodeManager.GetNode(remoteNode.Name);
            if (localNode == null || remoteNode.Incarnation > localNode.Incarnation)
            {
                _nodeManager.AddOrUpdateNode(remoteNode);
            }
        }
        
        // Return our state
        return Task.FromResult(_nodeManager.GetAllNodes());
    }
}
