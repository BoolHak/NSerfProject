// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Schedules and manages broadcast transmissions.
/// </summary>
public class BroadcastScheduler(TransmitLimitedQueue queue, ILogger? logger = null)
{
    private readonly TransmitLimitedQueue _queue = queue;
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Queues a broadcast for transmission.
    /// </summary>
    public void QueueBroadcast(IBroadcast broadcast)
    {
        _queue.QueueBroadcast(broadcast);
        _logger?.LogDebug("Queued broadcast");
    }

    /// <summary>
    /// Gets broadcasts to piggyback on a message.
    /// </summary>
    public List<byte[]> GetPiggybackBroadcasts(int overhead, int limit)
    {
        return _queue.GetBroadcasts(overhead, limit);
    }

    /// <summary>
    /// Prunes old broadcasts from the queue.
    /// </summary>
    public void PruneOldBroadcasts(int maxRetain)
    {
        _queue.Prune(maxRetain);
    }

    /// <summary>
    /// Gets the number of queued broadcasts.
    /// </summary>
    public int QueuedCount => _queue.NumQueued();
}
