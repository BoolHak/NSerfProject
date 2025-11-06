// Ported from: github.com/hashicorp/memberlist/util.go and state.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages node state operations including dead node cleanup and shuffling.
/// </summary>
public static class NodeStateManager
{
    /// <summary>
    /// Moves dead and left nodes that have not changed during the gossipToTheDeadTime interval
    /// to the end of the slice and returns the index of the first moved node.
    /// </summary>
    public static int MoveDeadNodes(List<NodeState> nodes, TimeSpan gossipToTheDeadTime)
    {
        var numDead = 0;
        var n = nodes.Count;
        
        for (var i = 0; i < n - numDead; i++)
        {
            if (!nodes[i].DeadOrLeft() || DateTimeOffset.UtcNow - nodes[i].StateChange <= gossipToTheDeadTime) 
                continue;
            
            // Move this node to the end
            (nodes[i], nodes[n - numDead - 1]) = (nodes[n - numDead - 1], nodes[i]);
            numDead++;
            i--;
        }
        
        return n - numDead;
    }
    
    /// <summary>
    /// Randomly shuffles the input nodes using the Fisher-Yates shuffle algorithm.
    /// </summary>
    public static void ShuffleNodes(List<NodeState> nodes)
    {
        var n = nodes.Count;
        
        for (var i = n - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (nodes[i], nodes[j]) = (nodes[j], nodes[i]);
        }
    }
    
    /// <summary>
    /// Selects up to k random nodes, excluding any nodes where the exclude function returns true.
    /// It is possible that fewer than k nodes are returned.
    /// </summary>
    public static List<Node> KRandomNodes(int k, List<NodeState> nodes, Func<NodeState, bool>? exclude = null)
    {
        var n = nodes.Count;
        var kNodes = new List<Node>(k);
        
        // Probe up to 3*n times, with large n this is not necessary
        // since k << n, but with small n we want search to be exhaustive
        for (var i = 0; i < 3 * n && kNodes.Count < k; i++)
        {
            var idx = Random.Shared.Next(n);
            var state = nodes[idx];
            
            // Give the filter a shot at it
            if (exclude != null && exclude(state)) continue;
            
            // Check if we have this node already
            var found = kNodes.Any(t => state.Name == t.Name);

            if (found) continue;
            
            // Append the node
            kNodes.Add(state.ToNode());
        }
        
        return kNodes;
    }
    
    /// <summary>
    /// Returns a random offset between 0 and n-1.
    /// </summary>
    public static int RandomOffset(int n) => n == 0 ? 0 : Random.Shared.Next(n);
}
