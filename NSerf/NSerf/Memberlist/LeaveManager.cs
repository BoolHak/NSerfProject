// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages graceful leaving of the cluster.
/// </summary>
public class LeaveManager
{
    private readonly Memberlist _memberlist;
    private readonly ILogger? _logger;
    
    public LeaveManager(Memberlist memberlist, ILogger? logger = null)
    {
        _memberlist = memberlist;
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
            
            // Increment incarnation to override any other messages
            var incarnation = _memberlist.NextIncarnation();
            
            // Create dead message for ourselves
            var deadMsg = new Dead
            {
                Incarnation = incarnation,
                Node = localNodeName,
                From = localNodeName
            };
            
            // CRITICAL: Force immediate gossip to send the queued broadcasts
            // Do 3 rounds - queue then gossip each time (background task may consume between rounds)
            // Use CancellationToken.None so shutdown doesn't cancel UDP writes
            _logger?.LogInformation("[LEAVE] Forcing 3 gossip rounds for leave broadcast");
            
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    // Queue the dead message before each gossip round
                    // Background task may have consumed previous broadcast
                    _memberlist.EncodeAndBroadcast(localNodeName, MessageType.Dead, deadMsg);
                    
                    // Gossip immediately to send the queued broadcast
                    await _memberlist.GossipAsync(CancellationToken.None);
                    
                    // Wait between gossip rounds for transmission
                    if (i < 2)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[LEAVE] Error during gossip round {Round}", i + 1);
                }
            }
            
            var finalQueued = _memberlist._broadcasts.NumQueued();
            _logger?.LogInformation("[LEAVE] Leave broadcast complete, {Remaining} still queued", finalQueued);
            
            // Brief wait to allow network transmission and retransmits
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            _logger?.LogInformation("[LEAVE] Leave broadcast complete");
            
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
