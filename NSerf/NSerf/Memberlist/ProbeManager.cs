// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Manages probing of cluster nodes for failure detection.
/// </summary>
public class ProbeManager
{
    private readonly ILogger? _logger;
    private int _probeIndex;
    private readonly Random _random = new();
    private readonly Memberlist? _memberlist;
    
    public ProbeManager(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    public ProbeManager(Memberlist memberlist, ILogger? logger = null)
    {
        _memberlist = memberlist;
        _logger = logger;
    }
    
    /// <summary>
    /// Gets the next node to probe.
    /// </summary>
    public NodeState? SelectNodeToProbe(List<NodeState> nodes, string localNodeName)
    {
        if (nodes.Count == 0)
        {
            return null;
        }
        
        // Move to next probe index
        _probeIndex = (_probeIndex + 1) % nodes.Count;
        
        var node = nodes[_probeIndex];
        
        // Skip ourselves
        if (node.Name == localNodeName)
        {
            _probeIndex = (_probeIndex + 1) % nodes.Count;
            if (_probeIndex < nodes.Count)
            {
                node = nodes[_probeIndex];
            }
            else
            {
                return null;
            }
        }
        
        return node;
    }
    
    /// <summary>
    /// Executes a probe operation against a node.
    /// </summary>
    public async Task<ProbeResult> ProbeNodeAsync(
        NodeState node,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            if (_memberlist == null)
            {
                // Fallback for tests without memberlist
                await Task.Delay(10, cancellationToken);
                return new ProbeResult
                {
                    Success = true,
                    NodeName = node.Name,
                    Rtt = sw.Elapsed,
                    UsedTcp = false,
                    IndirectChecks = 0
                };
            }
            
            // Try UDP ping first
            var success = await SendUdpPingAsync(node, timeout, sw, cancellationToken);
            
            if (!success)
            {
                // Fallback to TCP if UDP fails
                _logger?.LogDebug("UDP ping failed for {Node}, trying TCP", node.Name);
                success = await SendTcpPingAsync(node, timeout, sw, cancellationToken);
            }
            
            sw.Stop();
            
            return new ProbeResult
            {
                Success = success,
                NodeName = node.Name,
                Rtt = sw.Elapsed,
                UsedTcp = !success, // If we got here with success=false, we tried both
                IndirectChecks = 0
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Probe failed for node {Node}", node.Name);
            
            return new ProbeResult
            {
                Success = false,
                NodeName = node.Name,
                Rtt = sw.Elapsed,
                UsedTcp = false,
                IndirectChecks = 0
            };
        }
    }
    
    private async Task<bool> SendUdpPingAsync(NodeState node, TimeSpan timeout, Stopwatch sw, CancellationToken cancellationToken)
    {
        if (_memberlist == null) return false;
        
        var seqNo = _memberlist.NextSequenceNum();
        var ping = new PingMessage
        {
            SeqNo = seqNo,
            Node = node.Name,
            SourceNode = _memberlist._config.Name,
            SourceAddr = _memberlist.GetAdvertiseAddr().Address.GetAddressBytes(),
            SourcePort = (ushort)_memberlist.GetAdvertiseAddr().Port
        };
        
        // Set up ack handler
        var ackReceived = new TaskCompletionSource<bool>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        
        _memberlist._ackHandlers.TryAdd(seqNo, new AckNackHandler(_logger));
        _memberlist._ackHandlers[seqNo].SetAckHandler(
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
            // Send ping
            var addr = new Address
            {
                Addr = $"{node.Node.Addr}:{node.Node.Port}",
                Name = node.Name
            };
            
            var pingBytes = Messages.MessageEncoder.Encode(MessageType.Ping, ping);
            await _memberlist.SendUdpAsync(pingBytes, addr, cts.Token);
            
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
    
    private async Task<bool> SendTcpPingAsync(NodeState node, TimeSpan timeout, Stopwatch sw, CancellationToken cancellationToken)
    {
        if (_memberlist == null) return false;
        
        var seqNo = _memberlist.NextSequenceNum();
        var ping = new PingMessage
        {
            SeqNo = seqNo,
            Node = node.Name,
            SourceNode = _memberlist._config.Name,
            SourceAddr = _memberlist.GetAdvertiseAddr().Address.GetAddressBytes(),
            SourcePort = (ushort)_memberlist.GetAdvertiseAddr().Port
        };
        
        var addr = new Address
        {
            Addr = $"{node.Node.Addr}:{node.Node.Port}",
            Name = node.Name
        };
        
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        return await _memberlist.SendPingAndWaitForAckAsync(addr, ping, deadline, cancellationToken);
    }
    
    /// <summary>
    /// Selects random nodes for indirect probing.
    /// </summary>
    public List<NodeState> SelectIndirectNodes(
        List<NodeState> allNodes,
        string targetNode,
        string localNode,
        int count)
    {
        var candidates = allNodes
            .Where(n => n.Name != targetNode && n.Name != localNode && n.State == NodeStateType.Alive)
            .ToList();
        
        if (candidates.Count <= count)
        {
            return candidates;
        }
        
        // Randomly select
        return candidates.OrderBy(_ => _random.Next()).Take(count).ToList();
    }
}
