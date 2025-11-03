// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Estimates round-trip time for network operations.
/// </summary>
public class RttEstimator(int maxEntries = 1000)
{
    private readonly Dictionary<string, TimeSpan> _rttCache = [];
    private readonly object _lock = new();
    private readonly int _maxEntries = maxEntries;

    /// <summary>
    /// Records an RTT measurement for a node.
    /// </summary>
    public void RecordRtt(string nodeId, TimeSpan rtt)
    {
        lock (_lock)
        {
            _rttCache[nodeId] = rtt;

            // Prune if too large
            if (_rttCache.Count > _maxEntries)
            {
                var toRemove = _rttCache.OrderBy(kvp => kvp.Value).Skip(_maxEntries / 2).Select(kvp => kvp.Key).ToList();
                foreach (var key in toRemove)
                {
                    _rttCache.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Gets the estimated RTT for a node.
    /// </summary>
    public TimeSpan? GetRtt(string nodeId)
    {
        lock (_lock)
        {
            return _rttCache.TryGetValue(nodeId, out var rtt) ? rtt : null;
        }
    }

    /// <summary>
    /// Clears all RTT data.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _rttCache.Clear();
        }
    }
}
