// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages state transitions for nodes (alive -> suspect -> dead).
/// </summary>
public class StateTransitionManager
{
    private readonly ILogger? _logger;
    private readonly IEventDelegate? _eventDelegate;
    
    public StateTransitionManager(IEventDelegate? eventDelegate = null, ILogger? logger = null)
    {
        _eventDelegate = eventDelegate;
        _logger = logger;
    }
    
    /// <summary>
    /// Transitions a node to alive state.
    /// </summary>
    public void TransitionToAlive(NodeState node, uint incarnation)
    {
        var oldState = node.State;
        node.State = NodeStateType.Alive;
        node.Incarnation = incarnation;
        node.StateChange = DateTimeOffset.UtcNow;
        
        _logger?.LogInformation("Node {Node} transitioned {OldState} -> Alive (incarnation: {Inc})",
            node.Name, oldState, incarnation);
        
        if (oldState != NodeStateType.Alive)
        {
            _eventDelegate?.NotifyJoin(node.ToNode());
        }
    }
    
    /// <summary>
    /// Transitions a node to suspect state.
    /// </summary>
    public void TransitionToSuspect(NodeState node, uint incarnation, string from)
    {
        var oldState = node.State;
        node.State = NodeStateType.Suspect;
        node.Incarnation = incarnation;
        node.StateChange = DateTimeOffset.UtcNow;
        
        _logger?.LogWarning("Node {Node} transitioned {OldState} -> Suspect (from: {From}, incarnation: {Inc})",
            node.Name, oldState, from, incarnation);
    }
    
    /// <summary>
    /// Transitions a node to dead state.
    /// </summary>
    public void TransitionToDead(NodeState node, uint incarnation, string from)
    {
        var oldState = node.State;
        node.State = NodeStateType.Dead;
        node.Incarnation = incarnation;
        node.StateChange = DateTimeOffset.UtcNow;
        
        _logger?.LogError("Node {Node} transitioned {OldState} -> Dead (from: {From}, incarnation: {Inc})",
            node.Name, oldState, from, incarnation);
        
        _eventDelegate?.NotifyLeave(node.ToNode());
    }
    
    /// <summary>
    /// Transitions a node to left state.
    /// </summary>
    public void TransitionToLeft(NodeState node)
    {
        var oldState = node.State;
        node.State = NodeStateType.Left;
        node.StateChange = DateTimeOffset.UtcNow;
        
        _logger?.LogInformation("Node {Node} transitioned {OldState} -> Left",
            node.Name, oldState);
        
        _eventDelegate?.NotifyLeave(node.ToNode());
    }
    
    /// <summary>
    /// Checks if a state transition is valid.
    /// </summary>
    public bool IsValidTransition(NodeStateType from, NodeStateType to, uint oldIncarnation, uint newIncarnation)
    {
        // Same incarnation: no transition unless it's an upgrade
        if (oldIncarnation == newIncarnation)
        {
            return to > from;
        }
        
        // Higher incarnation: always valid
        return newIncarnation > oldIncarnation;
    }
}
