// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Handles direct ping operations.
/// </summary>
public class DirectPing
{
    private readonly ITransport _transport;
    private readonly ILogger? _logger;
    private readonly SequenceGenerator _seqGen;
    
    public DirectPing(ITransport transport, SequenceGenerator seqGen, ILogger? logger = null)
    {
        _transport = transport;
        _seqGen = seqGen;
        _logger = logger;
    }
    
    /// <summary>
    /// Sends a ping to a node and waits for ack.
    /// </summary>
    public async Task<PingResponse> PingAsync(
        Address target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var seqNo = _seqGen.NextSeqNo();
        var sw = Stopwatch.StartNew();
        
        try
        {
            // TODO: Send actual ping message
            _logger?.LogDebug("Sending ping {SeqNo} to {Target}", seqNo, target.Name);
            
            // TODO: Wait for ack with timeout
            await Task.Delay(10, cancellationToken);
            
            sw.Stop();
            
            return new PingResponse
            {
                Success = true,
                Rtt = sw.Elapsed,
                Payload = null
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new PingResponse
            {
                Success = false,
                Rtt = sw.Elapsed,
                Error = "Timeout"
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogWarning(ex, "Ping failed to {Target}", target.Name);
            
            return new PingResponse
            {
                Success = false,
                Rtt = sw.Elapsed,
                Error = ex.Message
            };
        }
    }
}
