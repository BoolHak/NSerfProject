// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NSerf.Memberlist;

/// <summary>
/// Handles acknowledgment and negative acknowledgment responses.
/// </summary>
public class AckNackHandler(ILogger? logger = null)
{
    private readonly ConcurrentDictionary<uint, AckHandler> _handlers = new();

    /// <summary>
    /// Sets an ack handler for a sequence number.
    /// </summary>
    public void SetAckHandler(uint seqNo, Action<byte[], DateTimeOffset> ackFn, Action? nackFn, TimeSpan timeout)
    {
        var handler = new AckHandler
        {
            AckFn = ackFn,
            NackFn = nackFn,
            Timer = new Timer(_ =>
            {
                if (!_handlers.TryRemove(seqNo, out var h)) return;
                h.NackFn?.Invoke();
                h.Dispose();
            }, null, timeout, Timeout.InfiniteTimeSpan)
        };

        _handlers[seqNo] = handler;
    }

    /// <summary>
    /// Invokes ack handler for a sequence number.
    /// </summary>
    public void InvokeAck(uint seqNo, byte[] payload, DateTimeOffset timestamp)
    {
        if (!_handlers.TryRemove(seqNo, out var handler)) return;
        handler.Timer?.Dispose();
        handler.AckFn?.Invoke(payload, timestamp);
        handler.Dispose();
    }

    /// <summary>
    /// Invokes nack handler for a sequence number.
    /// </summary>
    public void InvokeNack(uint seqNo)
    {
        if (!_handlers.TryRemove(seqNo, out var handler)) return;
        handler.Timer?.Dispose();
        handler.NackFn?.Invoke();
        handler.Dispose();
    }

    /// <summary>
    /// Clears all handlers.
    /// </summary>
    public void Clear()
    {
        foreach (var handler in _handlers.Values)
        {
            handler.Dispose();
        }
        _handlers.Clear();
        logger?.LogDebug("Cleared all handlers");
    }

    /// <summary>
    /// Gets the count of pending handlers.
    /// </summary>
    public int PendingCount => _handlers.Count;
}
