// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

namespace NSerf.Serf;

/// <summary>
/// MemberStatus is the state that a member is in.
/// Represents the lifecycle state of a cluster member.
/// </summary>
public enum MemberStatus
{
    /// <summary>
    /// StatusNone indicates no status / not found.
    /// </summary>
    None = 0,

    /// <summary>
    /// StatusAlive indicates the member is alive and reachable.
    /// </summary>
    Alive = 1,

    /// <summary>
    /// StatusLeaving indicates the member is in the process of leaving the cluster.
    /// </summary>
    Leaving = 2,

    /// <summary>
    /// StatusLeft indicates the member has gracefully left the cluster.
    /// </summary>
    Left = 3,

    /// <summary>
    /// StatusFailed indicates the member has failed (unreachable/crashed).
    /// </summary>
    Failed = 4
}

/// <summary>
/// Extension methods for MemberStatus.
/// </summary>
public static class MemberStatusExtensions
{
    /// <summary>
    /// Returns a string representation of the member status.
    /// </summary>
    public static string ToStatusString(this MemberStatus status)
    {
        return status switch
        {
            MemberStatus.None => "none",
            MemberStatus.Alive => "alive",
            MemberStatus.Leaving => "leaving",
            MemberStatus.Left => "left",
            MemberStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, $"unknown MemberStatus: {(int)status}")
        };
    }
}
