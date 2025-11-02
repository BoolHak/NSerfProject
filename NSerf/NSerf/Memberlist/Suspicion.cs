// Ported from: github.com/hashicorp/memberlist/suspicion.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Manages the suspect timer for a node and provides an interface to accelerate
/// the timeout as we get more independent confirmations that a node is suspect.
/// </summary>
public class Suspicion : IDisposable
{
    private readonly object _lock = new();
    private int _n; // Number of independent confirmations (atomic)
    private readonly int _k; // Number of confirmations we'd like to see
    private readonly TimeSpan _min; // Minimum timer value
    private readonly TimeSpan _max; // Maximum timer value
    private readonly DateTimeOffset _start; // When we began the timer
    private readonly Timer _timer;
    private readonly Action<int> _timeoutFn;
    private readonly HashSet<string> _confirmations;
    private bool _disposed;
    
    /// <summary>
    /// Creates a new suspicion timer started with the max time, that will drive
    /// to the min time after seeing k or more confirmations. The from node will be
    /// excluded from confirmations since we might get our own suspicion message
    /// gossiped back to us. The minimum time will be used if no confirmations are
    /// called for (k &lt;= 0).
    /// </summary>
    /// <param name="from">Node that originated the suspicion (excluded from confirmations).</param>
    /// <param name="k">Number of confirmations needed to drive to minimum timeout.</param>
    /// <param name="min">Minimum timeout duration.</param>
    /// <param name="max">Maximum timeout duration.</param>
    /// <param name="timeoutFn">Function to call when timer expires, receives confirmation count.</param>
    public Suspicion(string from, int k, TimeSpan min, TimeSpan max, Action<int> timeoutFn)
    {
        _k = k;
        _min = min;
        _max = max;
        _timeoutFn = timeoutFn;
        _confirmations = new HashSet<string>();
        _n = 0;
        
        // Exclude the from node from any confirmations
        _confirmations.Add(from);
        
        // If there aren't any confirmations to be made then take the min time from the start
        var timeout = k < 1 ? min : max;
        
        // Capture start time before starting timer
        _start = DateTimeOffset.UtcNow;
        
        // Create timer (will call timeout function when it fires)
        _timer = new Timer(_ => _timeoutFn(Volatile.Read(ref _n)), null, timeout, Timeout.InfiniteTimeSpan);
    }
    
    /// <summary>
    /// Registers that a possibly new peer has also determined the given node is suspect.
    /// Returns true if this was new information, and false if it was a duplicate confirmation,
    /// or if we've got enough confirmations to hit the minimum.
    /// </summary>
    /// <param name="from">Node providing the confirmation.</param>
    /// <returns>True if this was new information, false otherwise.</returns>
    public bool Confirm(string from)
    {
        lock (_lock)
        {
            // If we've got enough confirmations then stop accepting them
            if (Volatile.Read(ref _n) >= _k)
            {
                return false;
            }
            
            // Only allow one confirmation from each possible peer
            if (_confirmations.Contains(from))
            {
                return false;
            }
            _confirmations.Add(from);
            
            // Increment confirmation count atomically
            var n = Interlocked.Increment(ref _n);
            
            // Compute the new timeout given the current number of confirmations
            var elapsed = DateTimeOffset.UtcNow - _start;
            var remaining = CalculateRemainingSuspicionTime(n, _k, elapsed, _min, _max);
            
            // Adjust the timer
            if (remaining > TimeSpan.Zero)
            {
                _timer.Change(remaining, Timeout.InfiniteTimeSpan);
            }
            else
            {
                // Fire immediately on a background thread
                Task.Run(() => _timeoutFn(n));
            }
            
            return true;
        }
    }
    
    /// <summary>
    /// Calculates the remaining time to wait before considering a node dead.
    /// The return value can be negative, indicating the timer should fire immediately.
    /// </summary>
    /// <param name="n">Number of confirmations received.</param>
    /// <param name="k">Number of confirmations desired.</param>
    /// <param name="elapsed">Time elapsed since timer started.</param>
    /// <param name="min">Minimum timeout.</param>
    /// <param name="max">Maximum timeout.</param>
    /// <returns>Remaining time until timeout.</returns>
    public static TimeSpan CalculateRemainingSuspicionTime(
        int n, int k, TimeSpan elapsed, TimeSpan min, TimeSpan max)
    {
        // Formula: timeout = max - (log(n+1) / log(k+1)) * (max - min)
        // This creates a logarithmic curve from max to min as confirmations increase
        var frac = Math.Log(n + 1.0) / Math.Log(k + 1.0);
        var raw = max.TotalSeconds - frac * (max.TotalSeconds - min.TotalSeconds);
        var timeout = TimeSpan.FromMilliseconds(Math.Floor(1000.0 * raw));
        
        if (timeout < min)
        {
            timeout = min;
        }
        
        // Take into account the time that has passed so far
        return timeout - elapsed;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _timer?.Dispose();
        }
    }
}
