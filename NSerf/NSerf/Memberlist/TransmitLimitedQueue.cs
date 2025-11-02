// Ported from: github.com/hashicorp/memberlist/queue.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Used to queue messages to broadcast to the cluster (via gossip) but limits
/// the number of transmits per message. It also prioritizes messages with lower
/// transmit counts (hence newer messages).
/// </summary>
public class TransmitLimitedQueue
{
    private readonly object _lock = new();
    private readonly SortedSet<LimitedBroadcast> _queue = new(new LimitedBroadcastComparer());
    private readonly Dictionary<string, LimitedBroadcast> _nameMap = new();
    private long _idGen;
    
    /// <summary>
    /// Returns the number of nodes in the cluster. Used to determine retransmit count.
    /// </summary>
    public Func<int> NumNodes { get; set; } = () => 1;
    
    /// <summary>
    /// Multiplier used to determine the maximum number of retransmissions attempted.
    /// </summary>
    public int RetransmitMult { get; set; } = 4;
    
    /// <summary>
    /// Enqueues a broadcast for transmission.
    /// </summary>
    public void QueueBroadcast(IBroadcast broadcast)
    {
        QueueBroadcastInternal(broadcast, 0);
    }
    
    private void QueueBroadcastInternal(IBroadcast broadcast, int initialTransmits)
    {
        lock (_lock)
        {
            // Generate unique ID
            if (_idGen == long.MaxValue)
            {
                _idGen = 1;
            }
            else
            {
                _idGen++;
            }
            
            var lb = new LimitedBroadcast
            {
                Transmits = initialTransmits,
                MsgLen = broadcast.Message().Length,
                Id = _idGen,
                Broadcast = broadcast
            };
            
            bool isUnique = broadcast is IUniqueBroadcast;
            
            // Check if this is a named broadcast
            if (broadcast is INamedBroadcast nb)
            {
                lb.Name = nb.Name();
                
                // Replace any existing broadcast with the same name
                if (_nameMap.TryGetValue(lb.Name, out var old))
                {
                    old.Broadcast.Finished();
                    _queue.Remove(old);
                }
            }
            else if (!isUnique)
            {
                // Check for invalidation (slow path)
                var toRemove = new List<LimitedBroadcast>();
                foreach (var cur in _queue)
                {
                    // Special broadcasts can only invalidate each other
                    if (cur.Broadcast is not INamedBroadcast && 
                        cur.Broadcast is not IUniqueBroadcast)
                    {
                        if (broadcast.Invalidates(cur.Broadcast))
                        {
                            cur.Broadcast.Finished();
                            toRemove.Add(cur);
                        }
                    }
                }
                
                foreach (var cur in toRemove)
                {
                    _queue.Remove(cur);
                    if (cur.Name != null)
                    {
                        _nameMap.Remove(cur.Name);
                    }
                }
            }
            
            // Add to queue
            _queue.Add(lb);
            if (lb.Name != null)
            {
                _nameMap[lb.Name] = lb;
            }
        }
    }
    
    /// <summary>
    /// Gets broadcasts up to a byte limit, applying per-message overhead.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        lock (_lock)
        {
            if (_queue.Count == 0)
            {
                return new List<byte[]>();
            }
            
            var numNodes = NumNodes();
            int transmitLimit = Common.MemberlistMath.RetransmitLimit(RetransmitMult, numNodes);
            
            int bytesUsed = 0;
            var toSend = new List<byte[]>();
            var toReinsert = new List<LimitedBroadcast>();
            var toRemove = new List<LimitedBroadcast>();
            
            // Process items by transmit count (lower first)
            foreach (var item in _queue)
            {
                int msgLen = item.Broadcast.Message().Length;
                
                // Check if it fits
                if (bytesUsed + overhead + msgLen > limit)
                {
                    continue;
                }
                
                var msg = item.Broadcast.Message();
                bytesUsed += overhead + msg.Length;
                toSend.Add(msg);
                toRemove.Add(item);
                
                // Check if we should retransmit
                if (item.Transmits + 1 >= transmitLimit)
                {
                    item.Broadcast.Finished();
                }
                else
                {
                    // Reinsert with incremented transmit count
                    var updated = new LimitedBroadcast
                    {
                        Transmits = item.Transmits + 1,
                        MsgLen = item.MsgLen,
                        Id = item.Id,
                        Broadcast = item.Broadcast,
                        Name = item.Name
                    };
                    toReinsert.Add(updated);
                }
            }
            
            // Remove processed items
            foreach (var item in toRemove)
            {
                _queue.Remove(item);
                if (item.Name != null)
                {
                    _nameMap.Remove(item.Name);
                }
            }
            
            // Reinsert items that need more transmits
            foreach (var item in toReinsert)
            {
                _queue.Add(item);
                if (item.Name != null)
                {
                    _nameMap[item.Name] = item;
                }
            }
            
            // Reset ID generator if queue is empty
            if (_queue.Count == 0)
            {
                _idGen = 0;
            }
            
            return toSend;
        }
    }
    
    /// <summary>
    /// Returns the number of queued messages.
    /// </summary>
    public int NumQueued()
    {
        lock (_lock)
        {
            return _queue.Count;
        }
    }
    
    /// <summary>
    /// Clears all queued messages. Should only be used for tests.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var item in _queue)
            {
                item.Broadcast.Finished();
            }
            
            _queue.Clear();
            _nameMap.Clear();
            _idGen = 0;
        }
    }
    
    /// <summary>
    /// Retains only the maxRetain latest messages, discarding the rest.
    /// </summary>
    public void Prune(int maxRetain)
    {
        lock (_lock)
        {
            while (_queue.Count > maxRetain)
            {
                // Remove oldest (highest transmit count)
                var oldest = _queue.Max;
                if (oldest == null) break;
                
                oldest.Broadcast.Finished();
                _queue.Remove(oldest);
                if (oldest.Name != null)
                {
                    _nameMap.Remove(oldest.Name);
                }
            }
        }
    }
}

/// <summary>
/// Internal wrapper for a broadcast with transmission metadata.
/// </summary>
internal class LimitedBroadcast
{
    public int Transmits { get; set; }
    public long MsgLen { get; set; }
    public long Id { get; set; }
    public IBroadcast Broadcast { get; set; } = null!;
    public string? Name { get; set; }
}

/// <summary>
/// Comparer for LimitedBroadcast that prioritizes by transmit count, then message length, then ID.
/// </summary>
internal class LimitedBroadcastComparer : IComparer<LimitedBroadcast>
{
    public int Compare(LimitedBroadcast? x, LimitedBroadcast? y)
    {
        if (x == null || y == null) return 0;
        
        // Primary: Lower transmit count comes first
        if (x.Transmits != y.Transmits)
        {
            return x.Transmits.CompareTo(y.Transmits);
        }
        
        // Secondary: Larger messages come first (within same transmit tier)
        if (x.MsgLen != y.MsgLen)
        {
            return y.MsgLen.CompareTo(x.MsgLen);
        }
        
        // Tertiary: Higher ID comes first (newer messages)
        return y.Id.CompareTo(x.Id);
    }
}
