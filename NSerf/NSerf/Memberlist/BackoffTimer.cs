// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Exponential backoff timer for retries.
/// </summary>
public class BackoffTimer(TimeSpan minDelay, TimeSpan maxDelay)
{
    private readonly TimeSpan _minDelay = minDelay;
    private readonly TimeSpan _maxDelay = maxDelay;
    private TimeSpan _currentDelay = minDelay;
    private int _attempts;

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
