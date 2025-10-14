// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages probing of cluster nodes for failure detection.
/// </summary>
public class ProbeManager
{
    private readonly ILogger? _logger;
    private int _probeIndex;
    private readonly Random _random = new();
    
    public ProbeManager(ILogger? logger = null)
    {
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
            // TODO: Actual ping implementation
            await Task.Delay(10, cancellationToken);
            
            sw.Stop();
            
            return new ProbeResult
            {
                Success = true,
                NodeName = node.Name,
                Rtt = sw.Elapsed,
                UsedTcp = false,
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
