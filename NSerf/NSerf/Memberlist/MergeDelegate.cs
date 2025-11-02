// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Helper for merge operations.
/// </summary>
public class MergeHelper
{
    /// <summary>
    /// Determines if two clusters should merge.
    /// </summary>
    public static bool ShouldMerge(List<Node> ourNodes, List<Node> theirNodes)
    {
        // Simple heuristic: merge if we share any nodes
        var ourSet = new HashSet<string>(ourNodes.Select(n => n.Name));
        return theirNodes.Any(n => ourSet.Contains(n.Name));
    }
}
