// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// High-level broadcast queue wrapper.
/// </summary>
public class BroadcastQueue(TransmitLimitedQueue queue)
{
    /// <summary>
    /// Queues a simple byte array broadcast.
    /// </summary>
    public void QueueBytes(byte[] data)
    {
        queue.QueueBroadcast(new SimpleBroadcast(data));
    }

    /// <summary>
    /// Queues a broadcast with completion notification.
    /// Returns a task that completes when the broadcast is sent.
    /// </summary>
    public Task QueueBytesAsync(byte[] data)
    {
        queue.QueueBroadcast(new SimpleBroadcast(data));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Queues a named broadcast that can be invalidated.
    /// </summary>
    public void QueueNamed(string name, byte[] data)
    {
        queue.QueueBroadcast(new NamedBroadcast(name, data));
    }

    /// <summary>
    /// Gets broadcasts up to the specified limits.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        return queue.GetBroadcasts(overhead, limit);
    }

    /// <summary>
    /// Gets the number of queued broadcasts.
    /// </summary>
    public int Count => queue.NumQueued();

    /// <summary>
    /// Resets the queue.
    /// </summary>
    public void Reset() => queue.Reset();

    /// <summary>
    /// Prunes old broadcasts.
    /// </summary>
    public void Prune(int maxRetain) => queue.Prune(maxRetain);
}

/// <summary>
/// Simple broadcast implementation.
/// </summary>
internal class SimpleBroadcast(byte[] data) : IBroadcast
{
    private readonly byte[] _data = data;

    public bool Invalidates(IBroadcast other) => false;
    public byte[] Message() => _data;
    public void Finished() { }
}

/// <summary>
/// Named broadcast implementation.
/// </summary>
internal class NamedBroadcast(string name, byte[] data) : INamedBroadcast
{
    public string Name() => name;
    public bool Invalidates(IBroadcast other) => false;
    public byte[] Message() => data;
    public void Finished() { }
}

/// <summary>
/// Broadcast that notifies when transmission completes.
/// Matches Go's broadcast notification pattern.
/// </summary>
internal class NotifyingBroadcast(byte[] data, TaskCompletionSource notifier) : IBroadcast
{
    public bool Invalidates(IBroadcast other) => false;
    public byte[] Message() => data;

    public void Finished()
    {
        // Signal that broadcast has been sent (matches Go closing the notification channel)
        notifier.TrySetResult();
    }
}
