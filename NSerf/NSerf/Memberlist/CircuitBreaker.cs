// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Simple circuit breaker for network operations.
/// </summary>
public class CircuitBreaker(int threshold = 5, TimeSpan? resetTimeout = null)
{
    private int _failureCount;
    private DateTimeOffset _lastFailure;
    private readonly TimeSpan _resetTimeout = resetTimeout ?? TimeSpan.FromMinutes(1);

    /// <summary>
    /// Records a successful operation.
    /// </summary>
    public void RecordSuccess()
    {
        Interlocked.Exchange(ref _failureCount, 0);
    }

    /// <summary>
    /// Records a failed operation.
    /// </summary>
    public void RecordFailure()
    {
        Interlocked.Increment(ref _failureCount);
        _lastFailure = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Checks if the circuit is open (too many failures).
    /// </summary>
    public bool IsOpen()
    {
        if (_failureCount < threshold)
        {
            return false;
        }

        // Check if the reset timeout has passed
        if (DateTimeOffset.UtcNow - _lastFailure <= _resetTimeout) return true;
        Interlocked.Exchange(ref _failureCount, 0);
        return false;

    }
}
