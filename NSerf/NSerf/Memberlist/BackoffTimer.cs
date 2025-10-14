// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Exponential backoff timer for retries.
/// </summary>
public class BackoffTimer
{
    private readonly TimeSpan _minDelay;
    private readonly TimeSpan _maxDelay;
    private TimeSpan _currentDelay;
    private int _attempts;
    
    public BackoffTimer(TimeSpan minDelay, TimeSpan maxDelay)
    {
        _minDelay = minDelay;
        _maxDelay = maxDelay;
        _currentDelay = minDelay;
    }
    
    /// <summary>
    /// Gets the next delay duration.
    /// </summary>
    public TimeSpan NextDelay()
    {
        var delay = _currentDelay;
        _attempts++;
        
        // Exponential backoff
        _currentDelay = TimeSpan.FromTicks(_currentDelay.Ticks * 2);
        if (_currentDelay > _maxDelay)
        {
            _currentDelay = _maxDelay;
        }
        
        return delay;
    }
    
    /// <summary>
    /// Resets the backoff to minimum delay.
    /// </summary>
    public void Reset()
    {
        _currentDelay = _minDelay;
        _attempts = 0;
    }
    
    /// <summary>
    /// Gets the number of attempts.
    /// </summary>
    public int Attempts => _attempts;
}
