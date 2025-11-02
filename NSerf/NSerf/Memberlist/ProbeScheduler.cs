// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Schedules periodic probe operations.
/// </summary>
public class ProbeScheduler
{
    private readonly TimeSpan _interval;
    private readonly SwimProtocol _swimProtocol;
    private readonly ILogger? _logger;
    private Timer? _timer;
    private bool _isRunning;
    
    public ProbeScheduler(
        TimeSpan interval,
        SwimProtocol swimProtocol,
        ILogger? logger = null)
    {
        _interval = interval;
        _swimProtocol = swimProtocol;
        _logger = logger;
    }
    
    /// <summary>
    /// Starts the probe scheduler.
    /// </summary>
    public void Start(Func<Task> probeAction)
    {
        if (_isRunning)
        {
            return;
        }
        
        _isRunning = true;
        _timer = new Timer(async _ =>
        {
            if (_isRunning)
            {
                await probeAction();
            }
        }, null, _interval, _interval);
        
        _logger?.LogInformation("Probe scheduler started with interval {Interval}", _interval);
    }
    
    /// <summary>
    /// Stops the probe scheduler.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
        {
            return;
        }
        
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        
        _logger?.LogInformation("Probe scheduler stopped");
    }
    
    /// <summary>
    /// Gets whether the scheduler is running.
    /// </summary>
    public bool IsRunning => _isRunning;
}
