// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Handles push/pull full state synchronization.
/// </summary>
public class PushPullSynchronizer
{
    private readonly ILogger? _logger;
    private readonly Memberlist _memberlist;
    
    public PushPullSynchronizer(Memberlist memberlist, ILogger? logger = null)
    {
        _memberlist = memberlist;
        _logger = logger;
    }
    
    /// <summary>
    /// Initiates a push/pull sync with a random node.
    /// </summary>
    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        // Get all alive nodes excluding ourselves
        var nodes = _memberlist._nodes
            .Where(n => n.State == NodeStateType.Alive && n.Name != _memberlist._config.Name)
            .ToList();
            
        if (nodes.Count == 0)
        {
            return false;
        }
        
        var random = new Random();
        var target = nodes[random.Next(nodes.Count)];
        
        _logger?.LogDebug("Starting push/pull sync with {Node}", target.Name);
        
        try
        {
            // Create local state snapshot
            var localNodes = new List<PushNodeState>();
            lock (_memberlist._nodeLock)
            {
                foreach (var node in _memberlist._nodes)
                {
                    localNodes.Add(new PushNodeState
                    {
                        Name = node.Name,
                        Addr = node.Node.Addr.GetAddressBytes(),
                        Port = node.Node.Port,
                        Meta = node.Node.Meta,
                        Incarnation = node.Incarnation,
                        State = node.State,
                        Vsn = new byte[]
                        {
                            node.Node.PMin,
                            node.Node.PMax,
                            node.Node.PCur,
                            node.Node.DMin,
                            node.Node.DMax,
                            node.Node.DCur
                        }
                    });
                }
            }
            
            // In a full implementation, this would be sent over TCP to the target node
            // and we would receive their state in return. For now, we just acknowledge
            // that the sync was initiated. The actual TCP push/pull protocol would be
            // handled in a separate stream-based handler.
            
            _logger?.LogDebug("Push/pull sync initiated with {Node}, {Count} local nodes",
                target.Name, localNodes.Count);
            
            await Task.CompletedTask;
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
        List<PushNodeState> remoteNodes,
        CancellationToken cancellationToken = default)
    {
        _logger?.LogDebug("Handling push/pull request with {Count} remote nodes", remoteNodes.Count);
        
        // Merge remote state using StateHandlers
        var stateHandler = new StateHandlers(_memberlist, _logger);
        stateHandler.MergeRemoteState(remoteNodes);
        
        // Return our current state
        List<NodeState> localStates;
        lock (_memberlist._nodeLock)
        {
            localStates = new List<NodeState>(_memberlist._nodes);
        }
        
        return Task.FromResult(localStates);
    }
}
