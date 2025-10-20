// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf.Helpers;

/// <summary>
/// Provides utilities for query parameter calculation and configuration.
/// Handles default query timeout computation based on cluster size.
/// </summary>
public class SerfQueryHelper
{
    private readonly Func<int> _getMemberCount;
    private readonly Func<TimeSpan> _getGossipInterval;
    private readonly int _queryTimeoutMult;

    /// <summary>
    /// Creates a new SerfQueryHelper with the specified configuration.
    /// </summary>
    /// <param name="getMemberCount">Function to get current cluster member count</param>
    /// <param name="getGossipInterval">Function to get gossip interval</param>
    /// <param name="queryTimeoutMult">Query timeout multiplier</param>
    public SerfQueryHelper(
        Func<int> getMemberCount,
        Func<TimeSpan> getGossipInterval,
        int queryTimeoutMult)
    {
        _getMemberCount = getMemberCount ?? throw new ArgumentNullException(nameof(getMemberCount));
        _getGossipInterval = getGossipInterval ?? throw new ArgumentNullException(nameof(getGossipInterval));
        _queryTimeoutMult = queryTimeoutMult;
    }

    /// <summary>
    /// Calculates the default timeout value for a query.
    /// Formula: GossipInterval * QueryTimeoutMult * log(N+1)
    /// where N is the current cluster size.
    /// </summary>
    /// <returns>Calculated timeout duration</returns>
    public TimeSpan CalculateDefaultQueryTimeout()
    {
        // Determine current cluster size N
        int n = _getMemberCount();
        if (n < 1) n = 1; // Minimum of 1

        // Base gossip interval and multiplier
        var gossip = _getGossipInterval();
        int mult = _queryTimeoutMult;

        // Factor = ceil(log10(N+1)), minimum 1
        int factor = (int)Math.Ceiling(Math.Log10(n + 1));
        if (factor <= 0) factor = 1;

        // Compute as ticks to avoid TimeSpan arithmetic limitations
        long ticks = gossip.Ticks * mult * factor;
        return new TimeSpan(ticks);
    }

    /// <summary>
    /// Creates default query parameters with computed timeout.
    /// </summary>
    /// <returns>Default query parameters</returns>
    public QueryParam CreateDefaultQueryParams()
    {
        return new QueryParam
        {
            FilterNodes = null,
            FilterTags = null,
            RequestAck = false,
            Timeout = CalculateDefaultQueryTimeout()
        };
    }

    /// <summary>
    /// Static helper to calculate query timeout without creating an instance.
    /// </summary>
    /// <param name="memberCount">Current cluster member count</param>
    /// <param name="gossipInterval">Gossip interval</param>
    /// <param name="queryTimeoutMult">Query timeout multiplier</param>
    /// <returns>Calculated timeout duration</returns>
    public static TimeSpan CalculateQueryTimeout(
        int memberCount,
        TimeSpan gossipInterval,
        int queryTimeoutMult)
    {
        // Ensure minimum of 1 member
        if (memberCount < 1) memberCount = 1;

        // Factor = ceil(log10(N+1)), minimum 1
        int factor = (int)Math.Ceiling(Math.Log10(memberCount + 1));
        if (factor <= 0) factor = 1;

        // Compute as ticks to avoid TimeSpan arithmetic limitations
        long ticks = gossipInterval.Ticks * queryTimeoutMult * factor;
        return new TimeSpan(ticks);
    }
}
