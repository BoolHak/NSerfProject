// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf;

/// <summary>
/// Status of a member in the cluster.
/// Minimal implementation for Phase 0 - will be expanded in Phase 1.
/// </summary>
public enum MemberStatus
{
    /// <summary>
    /// No status / not found.
    /// </summary>
    None = 0,

    /// <summary>
    /// Member is alive and reachable.
    /// </summary>
    Alive = 1,

    /// <summary>
    /// Member is leaving the cluster.
    /// </summary>
    Leaving = 2,

    /// <summary>
    /// Member has left the cluster.
    /// </summary>
    Left = 3,

    /// <summary>
    /// Member has failed (unreachable).
    /// </summary>
    Failed = 4
}
