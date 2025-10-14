// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Provides filtering predicates for nodes.
/// </summary>
public static class NodeFilter
{
    /// <summary>
    /// Returns a filter that excludes the local node.
    /// </summary>
    public static Func<NodeState, bool> ExcludeLocalNode(string localNodeName)
    {
        return n => n.Name == localNodeName;
    }
    
    /// <summary>
    /// Returns a filter that excludes dead nodes.
    /// </summary>
    public static Func<NodeState, bool> ExcludeDeadNodes()
    {
        return n => n.State == NodeStateType.Dead;
    }
    
    /// <summary>
    /// Returns a filter that only includes alive nodes.
    /// </summary>
    public static Func<NodeState, bool> OnlyAliveNodes()
    {
        return n => n.State != NodeStateType.Alive;
    }
    
    /// <summary>
    /// Returns a filter that excludes multiple node names.
    /// </summary>
    public static Func<NodeState, bool> ExcludeNodes(params string[] nodeNames)
    {
        var set = new HashSet<string>(nodeNames);
        return n => set.Contains(n.Name);
    }
    
    /// <summary>
    /// Combines multiple filters with OR logic.
    /// </summary>
    public static Func<NodeState, bool> CombineOr(params Func<NodeState, bool>[] filters)
    {
        return n =>
        {
            foreach (var filter in filters)
            {
                if (filter(n))
                {
                    return true;
                }
            }
            return false;
        };
    }
}
