// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Common;

/// <summary>
/// Mathematical utility functions for Memberlist protocol calculations.
/// </summary>
public static class MemberlistMath
{
    /// <summary>
    /// The minimum number of nodes before push/pull timing starts scaling.
    /// </summary>
    private const int PushPullScaleThreshold = 32;
    
    /// <summary>
    /// Returns a random offset between 0 and n (exclusive).
    /// </summary>
    /// <param name="n">Upper bound (exclusive).</param>
    /// <returns>Random value in range [0, n).</returns>
    public static int RandomOffset(int n)
    {
        if (n == 0)
        {
            return 0;
        }
        
        return (int)(Random.Shared.Next() % (uint)n);
    }
    
    /// <summary>
    /// Computes the timeout that should be used when a node is suspected.
    /// Formula: suspicionMult * log10(max(1, n)) * interval
    /// </summary>
    /// <param name="suspicionMult">Multiplier for the suspicion timeout.</param>
    /// <param name="n">Number of nodes in the cluster.</param>
    /// <param name="interval">Base probe interval.</param>
    /// <returns>Calculated suspicion timeout.</returns>
    public static TimeSpan SuspicionTimeout(int suspicionMult, int n, TimeSpan interval)
    {
        var nodeScale = Math.Max(1.0, Math.Log10(Math.Max(1.0, n)));
        
        // Multiply by 1000 to keep precision because TimeSpan is stored as long ticks
        var timeoutMs = suspicionMult * nodeScale * 1000 * interval.TotalSeconds / 1000;
        
        return TimeSpan.FromSeconds(timeoutMs);
    }
    
    /// <summary>
    /// Computes the limit of retransmissions for gossip messages.
    /// Formula: retransmitMult * ceil(log10(n + 1))
    /// </summary>
    /// <param name="retransmitMult">Multiplier for retransmissions.</param>
    /// <param name="n">Number of nodes in the cluster.</param>
    /// <returns>Maximum number of retransmissions.</returns>
    public static int RetransmitLimit(int retransmitMult, int n)
    {
        var nodeScale = Math.Ceiling(Math.Log10(n + 1));
        return retransmitMult * (int)nodeScale;
    }
    
    /// <summary>
    /// Scales the time interval for push/pull syncs based on cluster size.
    /// This prevents network saturation as the cluster grows.
    /// </summary>
    /// <param name="interval">Base interval for push/pull.</param>
    /// <param name="n">Number of nodes in the cluster.</param>
    /// <returns>Scaled interval.</returns>
    public static TimeSpan PushPullScale(TimeSpan interval, int n)
    {
        // Don't scale until we cross the threshold
        if (n <= PushPullScaleThreshold)
        {
            return interval;
        }
        
        // Calculate multiplier: ceil(log2(n) - log2(threshold)) + 1
        var multiplier = Math.Ceiling(
            Math.Log2(n) - Math.Log2(PushPullScaleThreshold)
        ) + 1.0;
        
        return TimeSpan.FromTicks((long)(interval.Ticks * multiplier));
    }
}
