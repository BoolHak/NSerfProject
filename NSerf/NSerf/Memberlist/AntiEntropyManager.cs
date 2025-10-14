// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages anti-entropy mechanisms for state convergence.
/// </summary>
public class AntiEntropyManager
{
    private readonly PushPullSynchronizer _pushPullSync;
    private readonly ILogger? _logger;
    private DateTimeOffset _lastSync;
    
    public AntiEntropyManager(PushPullSynchronizer pushPullSync, ILogger? logger = null)
    {
        _pushPullSync = pushPullSync;
        _logger = logger;
        _lastSync = DateTimeOffset.UtcNow;
    }
    
    /// <summary>
    /// Performs a full state synchronization with a random node.
    /// </summary>
    public async Task<bool> PerformSyncAsync(CancellationToken cancellationToken = default)
    {
        var success = await _pushPullSync.SyncAsync(cancellationToken);
        
        if (success)
        {
            _lastSync = DateTimeOffset.UtcNow;
            _logger?.LogDebug("Anti-entropy sync completed successfully");
        }
        else
        {
            _logger?.LogWarning("Anti-entropy sync failed");
        }
        
        return success;
    }
    
    /// <summary>
    /// Gets the time since last successful sync.
    /// </summary>
    public TimeSpan TimeSinceLastSync => DateTimeOffset.UtcNow - _lastSync;
    
    /// <summary>
    /// Checks if sync is overdue.
    /// </summary>
    public bool IsSyncOverdue(TimeSpan maxInterval)
    {
        return TimeSinceLastSync > maxInterval;
    }
}
