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
    /// <summary>
    /// Queues a broadcast for transmission.
    /// </summary>
    public void QueueBroadcast(IBroadcast broadcast)
    {
        queue.QueueBroadcast(broadcast);
        logger?.LogDebug("Queued broadcast");
    }

    /// <summary>
    /// Gets broadcasts to piggyback on a message.
    /// </summary>
    public List<byte[]> GetPiggybackBroadcasts(int overhead, int limit)
    {
        return queue.GetBroadcasts(overhead, limit);
    }

    /// <summary>
    /// Prunes old broadcasts from the queue.
    /// </summary>
    public void PruneOldBroadcasts(int maxRetain)
    {
        queue.Prune(maxRetain);
    }

    /// <summary>
    /// Gets the number of queued broadcasts.
    /// </summary>
    public int QueuedCount => queue.NumQueued();
}
