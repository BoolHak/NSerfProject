// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Handles indirect ping operations through other nodes.
/// </summary>
public class IndirectPing
{
    private readonly ITransport _transport;
    private readonly SequenceGenerator _seqGen;
    private readonly ILogger? _logger;
    
    public IndirectPing(ITransport transport, SequenceGenerator seqGen, ILogger? logger = null)
    {
        _transport = transport;
        _seqGen = seqGen;
        _logger = logger;
    }
    
    /// <summary>
    /// Sends indirect ping requests to intermediary nodes.
    /// </summary>
    public async Task<bool> IndirectPingAsync(
        NodeState target,
        List<NodeState> intermediaries,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (intermediaries.Count == 0)
        {
            return false;
        }
        
        var seqNo = _seqGen.NextSeqNo();
        _logger?.LogDebug("Indirect ping {SeqNo} to {Target} via {Count} nodes",
            seqNo, target.Name, intermediaries.Count);
        
        var tasks = intermediaries.Select(node => 
            SendIndirectPingRequestAsync(node, target, seqNo, timeout, cancellationToken));
        
        // Wait for any success
        var results = await Task.WhenAll(tasks);
        return results.Any(r => r);
    }
    
    private async Task<bool> SendIndirectPingRequestAsync(
        NodeState intermediary,
        NodeState target,
        uint seqNo,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            // TODO: Send actual indirect ping request
            await Task.Delay(10, cancellationToken);
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Indirect ping request failed to {Node}", intermediary.Name);
            return false;
        }
    }
}
