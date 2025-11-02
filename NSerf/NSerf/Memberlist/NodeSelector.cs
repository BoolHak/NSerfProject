// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Selects nodes for various protocol operations.
/// </summary>
public class NodeSelector
{
    private readonly Random _random = new();
    
    /// <summary>
    /// Selects a random alive node.
    /// </summary>
    public NodeState? SelectRandomAlive(List<NodeState> nodes, string excludeNode)
    {
        var candidates = nodes
            .Where(n => n.State == NodeStateType.Alive && n.Name != excludeNode)
            .ToList();
        
        if (candidates.Count == 0)
        {
            return null;
        }
        
        return candidates[_random.Next(candidates.Count)];
    }
    
    /// <summary>
    /// Selects multiple random nodes for indirect probing.
    /// </summary>
    public List<NodeState> SelectIndirectProbeNodes(
        List<NodeState> nodes,
        string targetNode,
        string localNode,
        int count)
    {
        var candidates = nodes
            .Where(n => n.State == NodeStateType.Alive && 
                       n.Name != targetNode && 
                       n.Name != localNode)
            .ToList();
        
        if (candidates.Count <= count)
        {
            return candidates;
        }
        
        return RandomSelector.SelectRandomK(candidates, count);
    }
    
    /// <summary>
    /// Selects the next node to probe in round-robin fashion.
    /// </summary>
    public NodeState? SelectNextProbeNode(List<NodeState> nodes, ref int probeIndex, string localNode)
    {
        if (nodes.Count == 0)
        {
            return null;
        }
        
        probeIndex = (probeIndex + 1) % nodes.Count;
        var node = nodes[probeIndex];
        
        // Skip ourselves
        if (node.Name == localNode && nodes.Count > 1)
        {
            probeIndex = (probeIndex + 1) % nodes.Count;
            node = nodes[probeIndex];
        }
        
        return node.Name != localNode ? node : null;
    }
}
