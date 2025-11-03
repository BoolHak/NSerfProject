// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/broadcast.go

using System.Threading.Channels;
using NSerf.Memberlist;

namespace NSerf.Serf;

/// <summary>
/// Broadcast is an implementation of IBroadcast from memberlist and is used
/// to manage broadcasts across the memberlist channel that are related
/// only to Serf.
/// 
/// This class implements IUniqueBroadcast which means broadcasts are not
/// deduplicated - each broadcast is treated as unique.
/// </summary>
/// <remarks>
/// Creates a new broadcast with the specified message and notification channel.
/// </remarks>
/// <param name="msg">Message bytes to broadcast</param>
/// <param name="notifyWriter">Optional channel writer to notify when broadcast completes</param>
internal class Broadcast(byte[] msg, ChannelWriter<bool>? notifyWriter) : IUniqueBroadcast
{
    private readonly byte[] _msg = msg ?? throw new ArgumentNullException(nameof(msg));
    private readonly ChannelWriter<bool>? _notifyWriter = notifyWriter;

    /// <summary>
    /// Creates a new broadcast with the specified message.
    /// </summary>
    /// <param name="msg">Message bytes to broadcast</param>
    public Broadcast(byte[] msg) : this(msg, null)
    {
    }

    /// <summary>
    /// Invalidates checks if this broadcast invalidates another broadcast.
    /// For Serf broadcasts implementing IUniqueBroadcast, this always returns false,
    /// meaning broadcasts are never invalidated by newer ones.
    /// </summary>
    /// <param name="other">The other broadcast to check against</param>
    /// <returns>Always false for unique broadcasts</returns>
    public bool Invalidates(IBroadcast other)
    {
        // IUniqueBroadcast always returns false - broadcasts are never invalidated
        return false;
    }

    /// <summary>
    /// Returns the message bytes to be broadcast.
    /// </summary>
    /// <returns>Message byte array</returns>
    public byte[] Message()
    {
        return _msg;
    }

    /// <summary>
    /// Finished is called when the broadcast has been transmitted.
    /// If a notification channel was provided, it signals completion.
    /// </summary>
    public void Finished()
    {
        if (_notifyWriter != null)
        {
            // Try to write completion signal (non-blocking)
            _notifyWriter.TryWrite(true);

            // Complete the channel to signal no more writes
            // Using try/catch to handle multiple Complete() calls safely
            try
            {
                _notifyWriter.Complete();
            }
            catch (ChannelClosedException)
            {
                // Channel already closed, ignore
            }
        }
    }
}
