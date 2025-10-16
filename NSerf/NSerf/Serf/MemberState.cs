// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

namespace NSerf.Serf;

/// <summary>
/// MemberState is used to track members that are no longer active due to
/// leaving, failing, partitioning, etc. It tracks the member along with
/// when that member was marked as leaving.
/// </summary>
internal class MemberState
{
    /// <summary>
    /// The member information.
    /// </summary>
    public Member Member { get; set; } = new();

    /// <summary>
    /// Lamport clock time of last received message about this member.
    /// Used for cluster-wide ordering of events.
    /// </summary>
    public LamportTime StatusLTime { get; set; }

    /// <summary>
    /// Wall clock time when the member left/failed.
    /// Used for reaping old members and reconnect timeouts.
    /// </summary>
    public DateTime LeaveTime { get; set; }

    /// <summary>
    /// Creates a string representation of the member state.
    /// </summary>
    public override string ToString()
    {
        return $"{Member.Name} - Status: {Member.Status.ToStatusString()}, LTime: {StatusLTime}, LeaveTime: {LeaveTime}";
    }
}
