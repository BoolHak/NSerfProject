// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// A broadcast that has been queued with metadata.
/// </summary>
internal class QueuedBroadcast(IBroadcast broadcast)
{
    public IBroadcast Broadcast { get; set; } = broadcast;
    public int Transmits { get; set; } = 0;
    public DateTimeOffset QueueTime { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Compares broadcasts for ordering in the queue.
/// </summary>
internal class BroadcastComparer : IComparer<QueuedBroadcast>
{
    public int Compare(QueuedBroadcast? x, QueuedBroadcast? y)
    {
        if (x == null || y == null)
        {
            return 0;
        }

        // Lower transmits = higher priority
        var transmitCompare = x.Transmits.CompareTo(y.Transmits);
        return transmitCompare != 0 ? transmitCompare :
            // Older = higher priority
            x.QueueTime.CompareTo(y.QueueTime);
    }
}
