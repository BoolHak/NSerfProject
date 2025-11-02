// Ported from: github.com/hashicorp/memberlist/awareness.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Manages a simple metric for tracking the estimated health of the local node.
/// Health is primarily the node's ability to respond in the soft real-time manner
/// required for correct health checking of other nodes in the cluster.
/// </summary>
/// <remarks>
/// Creates a new awareness object.
/// </remarks>
/// <param name="maxMultiplier">Upper threshold for the timeout scale (score will be constrained to 0 &lt;= score &lt; max).</param>
public class Awareness(int maxMultiplier)
{
    private readonly object _lock = new();
    private readonly int _max = maxMultiplier;
    private int _score = 0;

    /// <summary>
    /// Takes the given delta and applies it to the score in a thread-safe manner.
    /// Enforces a floor of zero and a max of (max-1), so deltas may not change
    /// the overall score if it's railed at one of the extremes.
    /// </summary>
    /// <param name="delta">Amount to change the score by (can be positive or negative).</param>
    public void ApplyDelta(int delta)
    {
        lock (_lock)
        {
            _score = Math.Clamp(_score + delta, 0, _max - 1);
        }
    }

    /// <summary>
    /// Returns the raw health score. Lower values are healthier; zero is perfectly healthy.
    /// </summary>
    public int GetHealthScore()
    {
        lock (_lock)
        {
            return _score;
        }
    }

    /// <summary>
    /// Takes the given duration and scales it based on the current score.
    /// Less healthiness will lead to longer timeouts.
    /// </summary>
    /// <param name="timeout">Base timeout to scale.</param>
    /// <returns>Scaled timeout based on health score.</returns>
    public TimeSpan ScaleTimeout(TimeSpan timeout)
    {
        int score;
        lock (_lock)
        {
            score = _score;
        }

        // Formula: timeout * (score + 1)
        // Score 0: 1x timeout (healthy)
        // Score 1: 2x timeout
        // Score 7 (max for mult=8): 8x timeout (unhealthy)
        return timeout * (score + 1);
    }
}
