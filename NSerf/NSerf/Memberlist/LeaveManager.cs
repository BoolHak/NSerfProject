// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages graceful leaving of the cluster.
/// </summary>
public class LeaveManager
{
    private readonly BroadcastScheduler _broadcastScheduler;
    private readonly ILogger? _logger;
    
    public LeaveManager(BroadcastScheduler broadcastScheduler, ILogger? logger = null)
    {
        _broadcastScheduler = broadcastScheduler;
        _logger = logger;
    }
    
    /// <summary>
    /// Initiates a graceful leave from the cluster.
    /// </summary>
    public async Task<LeaveResult> LeaveAsync(
        string localNodeName,
        TimeSpan broadcastTimeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("Initiating graceful leave for {Node}", localNodeName);
            
            // TODO: Broadcast leave message
            // For now, just wait for broadcast timeout
            await Task.Delay(broadcastTimeout, cancellationToken);
            
            return new LeaveResult
            {
                Success = true,
                BroadcastTimeout = broadcastTimeout
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to leave gracefully");
            return new LeaveResult
            {
                Success = false,
                Error = ex,
                BroadcastTimeout = broadcastTimeout
            };
        }
    }
}
