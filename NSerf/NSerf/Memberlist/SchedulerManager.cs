// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Manages periodic scheduled tasks for memberlist.
/// </summary>
public class SchedulerManager
{
    private readonly List<Timer> _timers = new();
    private readonly CancellationTokenSource _stopTokenSource = new();
    private readonly object _lock = new();
    private bool _isScheduled;
    
    /// <summary>
    /// Schedules a periodic action.
    /// </summary>
    public void Schedule(TimeSpan interval, Action action, TimeSpan? initialDelay = null)
    {
        lock (_lock)
        {
            if (_stopTokenSource.IsCancellationRequested)
            {
                return;
            }
            
            var delay = initialDelay ?? TimeSpan.Zero;
            var timer = new Timer(_ =>
            {
                if (!_stopTokenSource.IsCancellationRequested)
                {
                    action();
                }
            }, null, delay, interval);
            
            _timers.Add(timer);
            _isScheduled = true;
        }
    }
    
    /// <summary>
    /// Stops all scheduled tasks.
    /// </summary>
    public void Deschedule()
    {
        lock (_lock)
        {
            if (!_isScheduled)
            {
                return;
            }
            
            _stopTokenSource.Cancel();
            
            foreach (var timer in _timers)
            {
                timer.Dispose();
            }
            
            _timers.Clear();
            _isScheduled = false;
        }
    }
    
    /// <summary>
    /// Gets whether tasks are currently scheduled.
    /// </summary>
    public bool IsScheduled
    {
        get
        {
            lock (_lock)
            {
                return _isScheduled;
            }
        }
    }
}
