// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Manages timers for node suspicions and other time-based operations.
/// </summary>
public class TimerPool
{
    private readonly Dictionary<string, IDisposable> _timers = new();
    private readonly object _lock = new();
    
    /// <summary>
    /// Adds or updates a timer for a node.
    /// </summary>
    public void SetTimer(string nodeId, IDisposable timer)
    {
        lock (_lock)
        {
            if (_timers.TryGetValue(nodeId, out var existing))
            {
                existing.Dispose();
            }
            _timers[nodeId] = timer;
        }
    }
    
    /// <summary>
    /// Removes and disposes a timer for a node.
    /// </summary>
    public void RemoveTimer(string nodeId)
    {
        lock (_lock)
        {
            if (_timers.Remove(nodeId, out var timer))
            {
                timer.Dispose();
            }
        }
    }
    
    /// <summary>
    /// Clears all timers.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }
            _timers.Clear();
        }
    }
    
    /// <summary>
    /// Gets the count of active timers.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _timers.Count;
            }
        }
    }
}
