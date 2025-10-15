// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
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
            Console.WriteLine($"[LEAVE] Initiating graceful leave for {localNodeName}");
            _logger?.LogInformation("Initiating graceful leave for {Node}", localNodeName);
            
            // Increment incarnation to override any other messages
            var incarnation = _memberlist.NextIncarnation();
            Console.WriteLine($"[LEAVE] Using incarnation {incarnation}");
            
            // Create dead message for ourselves
            var deadMsg = new Dead
            {
                Incarnation = incarnation,
                Node = localNodeName,
                From = localNodeName
            };
            
            Console.WriteLine($"[LEAVE] Broadcasting dead message for {localNodeName}");
            Console.WriteLine($"[LEAVE] Dead message: Node={deadMsg.Node}, From={deadMsg.From}, Inc={deadMsg.Incarnation}");
            Console.WriteLine($"[LEAVE] DeadNodeReclaimTime={_memberlist._config.DeadNodeReclaimTime}");
            Console.WriteLine($"[LEAVE] Broadcasts queued BEFORE: {_memberlist._broadcasts.NumQueued()}");
            // Broadcast the leave (dead) message
            _memberlist.EncodeAndBroadcast(localNodeName, MessageType.Dead, deadMsg);
            Console.WriteLine($"[LEAVE] Broadcast encode complete");
            // Check immediately before gossip can consume
            var queuedNow = _memberlist._broadcasts.NumQueued();
            Console.WriteLine($"[LEAVE] Broadcasts queued IMMEDIATELY after: {queuedNow}");
            
            // Wait for broadcast to propagate
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
