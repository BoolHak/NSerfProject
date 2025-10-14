// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Simple circuit breaker for network operations.
/// </summary>
public class CircuitBreaker
{
    private int _failureCount;
    private readonly int _threshold;
    private DateTimeOffset _lastFailure;
    private readonly TimeSpan _resetTimeout;
    
    public CircuitBreaker(int threshold = 5, TimeSpan? resetTimeout = null)
    {
        _threshold = threshold;
        _resetTimeout = resetTimeout ?? TimeSpan.FromMinutes(1);
    }
    
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
        if (_failureCount < _threshold)
        {
            return false;
        }
        
        // Check if reset timeout has passed
        if (DateTimeOffset.UtcNow - _lastFailure > _resetTimeout)
        {
            Interlocked.Exchange(ref _failureCount, 0);
            return false;
        }
        
        return true;
    }
}
