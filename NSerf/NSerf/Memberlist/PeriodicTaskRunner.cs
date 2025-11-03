// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Runs periodic maintenance tasks.
/// </summary>
public class PeriodicTaskRunner(ILogger? logger = null) : IDisposable
{
    private readonly List<(Timer Timer, string Name)> _timers = [];
    private readonly object _lock = new();
    private readonly ILogger? _logger = logger;
    private bool _disposed;

    /// <summary>
    /// Schedules a periodic task.
    /// </summary>
    public void Schedule(string name, TimeSpan interval, Action action)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PeriodicTaskRunner));
            }

            var timer = new Timer(_ =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error in periodic task {Task}", name);
                }
            }, null, interval, interval);

            _timers.Add((timer, name));
            _logger?.LogDebug("Scheduled periodic task {Task} with interval {Interval}", name, interval);
        }
    }

    /// <summary>
    /// Stops all periodic tasks.
    /// </summary>
    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var (timer, name) in _timers)
            {
                timer.Dispose();
                _logger?.LogDebug("Stopped periodic task {Task}", name);
            }
            _timers.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAll();
    }
}
