// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Tracks suspicion timers for nodes.
/// </summary>
public class SuspicionTracker
{
    private readonly Dictionary<string, Suspicion> _suspicions = [];
    private readonly object _lock = new();

    /// <summary>
    /// Adds a suspicion timer for a node.
    /// </summary>
    public void AddSuspicion(string nodeId, Suspicion suspicion)
    {
        lock (_lock)
        {
            if (_suspicions.TryGetValue(nodeId, out var existing))
            {
                existing.Dispose();
            }
            _suspicions[nodeId] = suspicion;
        }
    }

    /// <summary>
    /// Gets a suspicion timer for a node.
    /// </summary>
    public Suspicion? GetSuspicion(string nodeId)
    {
        lock (_lock)
        {
            return _suspicions.GetValueOrDefault(nodeId);
        }
    }

    /// <summary>
    /// Removes and disposes a suspicion timer.
    /// </summary>
    public void RemoveSuspicion(string nodeId)
    {
        lock (_lock)
        {
            if (_suspicions.Remove(nodeId, out var suspicion))
            {
                suspicion.Dispose();
            }
        }
    }

    /// <summary>
    /// Clears all suspicion timers.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var suspicion in _suspicions.Values)
            {
                suspicion.Dispose();
            }
            _suspicions.Clear();
        }
    }

    /// <summary>
    /// Gets the count of active suspicions.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _suspicions.Count;
            }
        }
    }
}
