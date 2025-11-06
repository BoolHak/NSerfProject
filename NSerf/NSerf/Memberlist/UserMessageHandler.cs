// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;

namespace NSerf.Memberlist;

/// <summary>
/// Handles user-defined messages for application-level communication.
/// </summary>
public class UserMessageHandler(IDelegate? delegateHandler = null, ILogger? logger = null)
{

    /// <summary>
    /// Processes an incoming user message.
    /// </summary>
    public void HandleUserMessage(byte[] message)
    {
        try
        {
            delegateHandler?.NotifyMsg(message);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error handling user message");
        }
    }

    /// <summary>
    /// Gets user messages to broadcast.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        try
        {
            return delegateHandler?.GetBroadcasts(overhead, limit) ?? [];
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error getting broadcasts from delegate");
            return [];
        }
    }
}
