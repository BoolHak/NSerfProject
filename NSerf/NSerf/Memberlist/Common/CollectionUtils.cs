// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Common;

/// <summary>
/// Collection utility functions for managing node lists.
/// </summary>
public static class CollectionUtils
{
    /// <summary>
    /// Randomly shuffles the input nodes using the Fisher-Yates shuffle.
    /// </summary>
    /// <param name="nodes">Array of nodes to shuffle in place.</param>
    public static void ShuffleNodes(NodeState[] nodes)
    {
        var n = nodes.Length;
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(0, i + 1);
            (nodes[i], nodes[j]) = (nodes[j], nodes[i]);
        }
    }
    
    /// <summary>
    /// Moves dead and left nodes that have not changed during the gossipToTheDeadTime interval
    /// to the end of the slice and returns the index of the first moved node.
    /// </summary>
    /// <param name="nodes">Array of node states to reorganize.</param>
    /// <param name="gossipToTheDeadTime">Time threshold for considering a node truly dead.</param>
    /// <returns>Index of the first dead/left node in the array.</returns>
    public static int MoveDeadNodes(NodeState[] nodes, TimeSpan gossipToTheDeadTime)
    {
        int numDead = 0;
        int n = nodes.Length;
        
        for (int i = 0; i < n - numDead; i++)
        {
            if (!nodes[i].DeadOrLeft())
            {
                continue;
            }
            
            // Respect the gossip to the dead interval
            if (DateTimeOffset.UtcNow - nodes[i].StateChange <= gossipToTheDeadTime)
            {
                continue;
            }
            
            // Move this node to the end
            (nodes[i], nodes[n - numDead - 1]) = (nodes[n - numDead - 1], nodes[i]);
            numDead++;
            i--;
        }
        
        return n - numDead;
    }
    
    /// <summary>
    /// Selects up to k random nodes, excluding any nodes where the exclude function returns true.
    /// It is possible that less than k nodes are returned.
    /// </summary>
    /// <param name="k">Maximum number of nodes to select.</param>
    /// <param name="nodes">Array of node states to choose from.</param>
    /// <param name="exclude">Function to filter out unwanted nodes.</param>
    /// <returns>List of selected nodes.</returns>
    public static List<Node> KRandomNodes(int k, NodeState[] nodes, Func<NodeState, bool>? exclude = null)
    {
        int n = nodes.Length;
        var kNodes = new List<Node>(k);
        
        // Probe up to 3*n times, with large n this is not necessary
        // since k << n, but with small n we want search to be exhaustive
        for (int i = 0; i < 3 * n && kNodes.Count < k; i++)
        {
            // Get random node state
            int idx = MemberlistMath.RandomOffset(n);
            var state = nodes[idx];
            
            // Give the filter a shot at it
            if (exclude != null && exclude(state))
            {
                continue;
            }
            
            // Check if we have this node already
            bool duplicate = false;
            for (int j = 0; j < kNodes.Count; j++)
            {
                if (state.Node.Name == kNodes[j].Name)
                {
                    duplicate = true;
                    break;
                }
            }
            
            if (duplicate)
            {
                continue;
            }
            
            // Append the node
            kNodes.Add(state.Node);
        }
        
        return kNodes;
    }
}
