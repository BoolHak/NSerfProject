// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;

namespace NSerf.Memberlist;

/// <summary>
/// Handles user-defined messages for application-level communication.
/// </summary>
public class UserMessageHandler
{
    private readonly IDelegate? _delegate;
    private readonly ILogger? _logger;
    
    public UserMessageHandler(IDelegate? delegateHandler = null, ILogger? logger = null)
    {
        _delegate = delegateHandler;
        _logger = logger;
    }
    
    /// <summary>
    /// Processes an incoming user message.
    /// </summary>
    public void HandleUserMessage(byte[] message)
    {
        try
        {
            _delegate?.NotifyMsg(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling user message");
        }
    }
    
    /// <summary>
    /// Gets user messages to broadcast.
    /// </summary>
    public List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        try
        {
            return _delegate?.GetBroadcasts(overhead, limit) ?? new List<byte[]>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error getting broadcasts from delegate");
            return new List<byte[]>();
        }
    }
}
