// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Manages gossip operations for spreading information.
/// </summary>
public class GossipManager(TransmitLimitedQueue broadcasts)
{
    /// <summary>
    /// Queues a broadcast for gossip.
    /// </summary>
    public void QueueBroadcast(IBroadcast broadcast)
    {
        broadcasts.QueueBroadcast(broadcast);
    }

    /// <summary>
    /// Gets broadcasts to send.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        return broadcasts.GetBroadcasts(overhead, limit);
    }

    /// <summary>
    /// Gets the number of queued broadcasts.
    /// </summary>
    public int NumQueued() => broadcasts.NumQueued();
}
