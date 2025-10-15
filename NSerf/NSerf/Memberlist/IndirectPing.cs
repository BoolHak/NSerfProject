// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Handles indirect ping operations through other nodes.
/// </summary>
public class IndirectPing
{
    private readonly Memberlist _memberlist;
    private readonly ILogger? _logger;
    
    public IndirectPing(Memberlist memberlist, ILogger? logger = null)
    {
        _memberlist = memberlist;
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
        
        var seqNo = _memberlist.NextSequenceNum();
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
            // Create indirect ping message
            var (addr, port) = _memberlist.GetAdvertiseAddr();
            var indirectPing = new IndirectPingMessage
            {
                SeqNo = seqNo,
                Target = target.Node.Addr.GetAddressBytes(),
                Port = target.Node.Port,
                Node = target.Name,
                SourceAddr = addr.GetAddressBytes(),
                SourcePort = (ushort)port,
                SourceNode = _memberlist._config.Name
            };
            
            // Set up ack handler to wait for response
            var ackReceived = new TaskCompletionSource<bool>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            
            var handler = new AckNackHandler(_logger);
            _memberlist._ackHandlers.TryAdd(seqNo, handler);
            handler.SetAckHandler(
                seqNo,
                (payload, timestamp) =>
                {
                    ackReceived.TrySetResult(true);
                },
                () =>
                {
                    ackReceived.TrySetResult(false);
                },
                timeout
            );
            
            try
            {
                // Send indirect ping request to intermediary
                var intermediaryAddr = new Address
                {
                    Addr = $"{intermediary.Node.Addr}:{intermediary.Node.Port}",
                    Name = intermediary.Name
                };
                
                var pingBytes = Messages.MessageEncoder.Encode(MessageType.IndirectPing, indirectPing);
                await _memberlist.SendUdpAsync(pingBytes, intermediaryAddr, cts.Token);
                
                // Wait for ack
                return await ackReceived.Task;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _memberlist._ackHandlers.TryRemove(seqNo, out _);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Indirect ping request failed to {Node}", intermediary.Name);
            return false;
        }
    }
}
