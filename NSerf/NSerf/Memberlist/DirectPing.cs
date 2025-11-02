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
public class DirectPing(Memberlist memberlist, SequenceGenerator seqGen, ILogger? logger = null)
{
    private readonly Memberlist _memberlist = memberlist;
    private readonly ILogger? _logger = logger;
    private readonly SequenceGenerator _seqGen = seqGen;

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
            _logger?.LogDebug("Sending ping {SeqNo} to {Target}", seqNo, target.Name);

            // Create ping message
            var ping = new Messages.PingMessage
            {
                SeqNo = seqNo,
                Node = _memberlist._config.Name
            };

            var deadline = DateTimeOffset.UtcNow + timeout;
            var success = await _memberlist.SendPingAndWaitForAckAsync(target, ping, deadline, cancellationToken);

            sw.Stop();

            return new PingResponse
            {
                Success = success,
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
