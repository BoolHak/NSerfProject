// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Thread-safe priority queue for messages.
/// </summary>
public class MessageQueue(int maxDepth = 1024)
{
    private readonly LinkedList<object> _highPriority = new();
    private readonly LinkedList<object> _lowPriority = new();
    private readonly object _lock = new();
    private readonly int _maxDepth = maxDepth;

    /// <summary>
    /// Enqueues a message with specified priority.
    /// </summary>
    public bool Enqueue(object message, bool highPriority = false)
    {
        lock (_lock)
        {
            var queue = highPriority ? _highPriority : _lowPriority;

            if (queue.Count >= _maxDepth)
            {
                return false;
            }

            queue.AddLast(message);
            return true;
        }
    }

    /// <summary>
    /// Dequeues the next message (high priority first).
    /// </summary>
    public bool TryDequeue(out object? message)
    {
        lock (_lock)
        {
            if (_highPriority.Count > 0)
            {
                message = _highPriority.Last!.Value;
                _highPriority.RemoveLast();
                return true;
            }

            if (_lowPriority.Count > 0)
            {
                message = _lowPriority.Last!.Value;
                _lowPriority.RemoveLast();
                return true;
            }

            message = null;
            return false;
        }
    }

    /// <summary>
    /// Gets the total count of queued messages.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _highPriority.Count + _lowPriority.Count;
            }
        }
    }
}
