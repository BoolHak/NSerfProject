// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// A broadcast that has been queued with metadata.
/// </summary>
internal class QueuedBroadcast
{
    public IBroadcast Broadcast { get; set; } = null!;
    public int Transmits { get; set; }
    public DateTimeOffset QueueTime { get; set; }
    
    public QueuedBroadcast(IBroadcast broadcast)
    {
        Broadcast = broadcast;
        Transmits = 0;
        QueueTime = DateTimeOffset.UtcNow;
    }
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
        if (transmitCompare != 0)
        {
            return transmitCompare;
        }
        
        // Older = higher priority
        return x.QueueTime.CompareTo(y.QueueTime);
    }
}
