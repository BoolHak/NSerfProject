// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf;

/// <summary>
/// Stats method for Serf class
/// </summary>
public partial class Serf
{
    /// <summary>
    /// Stats returns various statistics about the Serf agent.
    /// Maps to: Go's Stats() method in serf.go
    /// </summary>
    /// <returns>Dictionary of statistics</returns>
    public Dictionary<string, string> Stats()
    {
        // Get member counts by status using ExecuteUnderLock
        var counts = _memberManager.ExecuteUnderLock(accessor =>
        {
            var aliveMembers = accessor.GetMembersByStatus(MemberStatus.Alive);
            var failedMembers = accessor.GetMembersByStatus(MemberStatus.Failed);
            var leftMembers = accessor.GetMembersByStatus(MemberStatus.Left);
            return (Alive: aliveMembers.Count, Failed: failedMembers.Count, Left: leftMembers.Count);
        });
        
        var members = counts.Alive;
        var failed = counts.Failed;
        var left = counts.Left;

        // Get health score from memberlist
        var healthScore = Memberlist?.GetHealthScore() ?? 0;

        var stats = new Dictionary<string, string>
        {
            ["members"] = members.ToString(),
            ["failed"] = failed.ToString(),
            ["left"] = left.ToString(),
            ["health_score"] = healthScore.ToString(),
            ["member_time"] = Clock.Time().ToString(),
            ["event_time"] = EventClock.Time().ToString(),
            ["query_time"] = QueryClock.Time().ToString(),
            ["intent_queue"] = "0", // TODO: Implement once BroadcastQueue exposes queue size
            ["event_queue"] = "0",  // TODO: Implement once BroadcastQueue exposes queue size
            ["query_queue"] = "0",  // TODO: Implement once BroadcastQueue exposes queue size
            ["encrypted"] = EncryptionEnabled().ToString().ToLowerInvariant()
        };

        // Add coordinate statistics if enabled
        if (!Config.DisableCoordinates && _coordClient != null)
        {
            var coordStats = _coordClient.Stats();
            stats["coordinate_resets"] = coordStats.Resets.ToString();
        }

        return stats;
    }
}
