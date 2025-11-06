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
    private TimeSpan _currentDelay = minDelay;

    /// <summary>
    /// Gets the next delay duration.
    /// </summary>
    public TimeSpan NextDelay()
    {
        var delay = _currentDelay;
        Attempts++;

        // Exponential backoff
        _currentDelay = TimeSpan.FromTicks(_currentDelay.Ticks * 2);
        if (_currentDelay > maxDelay)
        {
            _currentDelay = maxDelay;
        }

        return delay;
    }

    /// <summary>
    /// Resets the backoff to minimum delay.
    /// </summary>
    public void Reset()
    {
        _currentDelay = _minDelay;
        Attempts = 0;
    }

    /// <summary>
    /// Gets the number of attempts.
    /// </summary>
    public int Attempts { get; private set; }
}
