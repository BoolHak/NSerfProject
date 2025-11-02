// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages gossip operations for spreading information.
/// </summary>
public class GossipManager
{
    private readonly TransmitLimitedQueue _broadcasts;
    private readonly ILogger? _logger;
    
    public GossipManager(TransmitLimitedQueue broadcasts, ILogger? logger = null)
    {
        _broadcasts = broadcasts;
        _logger = logger;
    }
    
    /// <summary>
    /// Queues a broadcast for gossip.
    /// </summary>
    public void QueueBroadcast(IBroadcast broadcast)
    {
        _broadcasts.QueueBroadcast(broadcast);
    }
    
    /// <summary>
    /// Gets broadcasts to send.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        return _broadcasts.GetBroadcasts(overhead, limit);
    }
    
    /// <summary>
    /// Gets the number of queued broadcasts.
    /// </summary>
    public int NumQueued() => _broadcasts.NumQueued();
}
