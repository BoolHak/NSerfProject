// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/query.go

namespace NSerf.Serf;

/// <summary>
/// Helper methods for query operations.
/// </summary>
public static class QueryHelpers
{
    /// <summary>
    /// kRandomMembers selects up to k members from a given list, optionally
    /// filtering by the given filterFunc.
    /// </summary>
    public static List<Member> KRandomMembers(int k, List<Member> members, Func<Member, bool>? filterFunc = null)
    {
        var n = members.Count;
        var kMembers = new List<Member>(k);
        var random = new Random();

        // Probe up to 3*n times, with large n this is not necessary
        // since k << n, but with small n we want search to be exhaustive
        for (var i = 0; i < 3 * n && kMembers.Count < k; i++)
        {
            // Get a random member
            var idx = random.Next(n);
            var member = members[idx];

            // Give the filter a shot at it
            if (filterFunc != null && filterFunc(member))
            {
                continue;
            }

            // Check if we have this member already
            if (kMembers.Any(m => m.Name == member.Name))
            {
                continue;
            }

            // Append the member
            kMembers.Add(member);
        }

        return kMembers;
    }
}
