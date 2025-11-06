// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Threading.Channels;

namespace NSerf.Memberlist;

/// <summary>
/// Provides notification when a broadcast completes.
/// </summary>
public class BroadcastNotifyChannel
{
    private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>();

    /// <summary>
    /// Notifies that a broadcast is complete.
    /// </summary>
    public void Notify()
    {
        _channel.Writer.TryWrite(true);
    }
    
    /// <summary>
    /// Waits for the broadcast to complete.
    /// </summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
    
    /// <summary>
    /// Gets the reader for the channel.
    /// </summary>
    public ChannelReader<bool> Reader => _channel.Reader;
}
