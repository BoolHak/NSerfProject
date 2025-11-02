// Ported from: github.com/hashicorp/memberlist/state.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.State;

/// <summary>
/// Represents the state of a node in the cluster.
/// </summary>
public enum NodeStateType
{
    /// <summary>
    /// Node is alive and responding to probes.
    /// </summary>
    Alive = 0,
    
    /// <summary>
    /// Node is suspected of being dead but not yet confirmed.
    /// </summary>
    Suspect = 1,
    
    /// <summary>
    /// Node is confirmed dead (failed to respond to probes).
    /// </summary>
    Dead = 2,
    
    /// <summary>
    /// Node has left the cluster gracefully.
    /// </summary>
    Left = 3
}

/// <summary>
/// Extension methods for NodeStateType.
/// </summary>
public static class NodeStateTypeExtensions
{
    /// <summary>
    /// Converts the NodeStateType to a metrics-friendly string.
    /// </summary>
    public static string ToMetricsString(this NodeStateType state)
    {
        return state switch
        {
            NodeStateType.Alive => "alive",
            NodeStateType.Dead => "dead",
            NodeStateType.Suspect => "suspect",
            NodeStateType.Left => "left",
            _ => $"unhandled-value-{(int)state}"
        };
    }
}
