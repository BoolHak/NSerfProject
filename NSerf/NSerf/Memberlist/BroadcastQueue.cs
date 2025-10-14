// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// High-level broadcast queue wrapper.
/// </summary>
public class BroadcastQueue
{
    private readonly TransmitLimitedQueue _queue;
    
    public BroadcastQueue(TransmitLimitedQueue queue)
    {
        _queue = queue;
    }
    
    /// <summary>
    /// Queues a simple byte array broadcast.
    /// </summary>
    public void QueueBytes(byte[] data)
    {
        _queue.QueueBroadcast(new SimpleBroadcast(data));
    }
    
    /// <summary>
    /// Queues a named broadcast that can be invalidated.
    /// </summary>
    public void QueueNamed(string name, byte[] data)
    {
        _queue.QueueBroadcast(new NamedBroadcast(name, data));
    }
    
    /// <summary>
    /// Gets broadcasts up to the specified limits.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        return _queue.GetBroadcasts(overhead, limit);
    }
    
    /// <summary>
    /// Gets the number of queued broadcasts.
    /// </summary>
    public int Count => _queue.NumQueued();
    
    /// <summary>
    /// Resets the queue.
    /// </summary>
    public void Reset() => _queue.Reset();
    
    /// <summary>
    /// Prunes old broadcasts.
    /// </summary>
    public void Prune(int maxRetain) => _queue.Prune(maxRetain);
}

/// <summary>
/// Simple broadcast implementation.
/// </summary>
internal class SimpleBroadcast : IBroadcast
{
    private readonly byte[] _data;
    
    public SimpleBroadcast(byte[] data)
    {
        _data = data;
    }
    
    public bool Invalidates(IBroadcast other) => false;
    public byte[] Message() => _data;
    public void Finished() { }
}

/// <summary>
/// Named broadcast implementation.
/// </summary>
internal class NamedBroadcast : INamedBroadcast
{
    private readonly string _name;
    private readonly byte[] _data;
    
    public NamedBroadcast(string name, byte[] data)
    {
        _name = name;
        _data = data;
    }
    
    public string Name() => _name;
    public bool Invalidates(IBroadcast other) => false;
    public byte[] Message() => _data;
    public void Finished() { }
}
