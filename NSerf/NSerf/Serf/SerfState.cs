// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

namespace NSerf.Serf;

/// <summary>
/// SerfState represents the current state of a Serf instance.
/// Tracks the lifecycle of the Serf node.
/// </summary>
public enum SerfState
{
    /// <summary>
    /// Serf is alive and operating normally.
    /// </summary>
    Alive = 0,

    /// <summary>
    /// Serf is in the process of leaving the cluster.
    /// </summary>
    Leaving = 1,

    /// <summary>
    /// Serf has left the cluster gracefully.
    /// </summary>
    Left = 2,

    /// <summary>
    /// Serf has been shut down.
    /// </summary>
    Shutdown = 3
}

/// <summary>
/// Extension methods for SerfState.
/// </summary>
public static class SerfStateExtensions
{
    /// <summary>
    /// Returns a string representation of the Serf state.
    /// </summary>
    public static string ToStateString(this SerfState state)
    {
        return state switch
        {
            SerfState.Alive => "alive",
            SerfState.Leaving => "leaving",
            SerfState.Left => "left",
            SerfState.Shutdown => "shutdown",
            _ => "unknown"
        };
    }
}
