// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Manages health scores and timeout scaling for memberlist.
/// </summary>
public class HealthScoreManager
{
    private readonly Awareness _awareness;
    private readonly int _maxMultiplier;
    
    public HealthScoreManager(Awareness awareness, int maxMultiplier = 8)
    {
        _awareness = awareness;
        _maxMultiplier = maxMultiplier;
    }
    
    /// <summary>
    /// Gets the current health score.
    /// </summary>
    public int GetHealthScore() => _awareness.GetHealthScore();
    
    /// <summary>
    /// Scales a timeout based on current health score.
    /// </summary>
    public TimeSpan ScaleTimeout(TimeSpan timeout)
    {
        var score = _awareness.GetHealthScore();
        if (score == 0)
        {
            return timeout;
        }
        
        var multiplier = Math.Min(score + 1, _maxMultiplier);
        return TimeSpan.FromTicks(timeout.Ticks * multiplier);
    }
}
