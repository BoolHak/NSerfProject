// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Implements the SWIM (Scalable Weakly-consistent Infection-style Process Group Membership) protocol.
/// </summary>
public class SwimProtocol
{
    private readonly MemberlistConfig _config;
    private readonly ProbeManager _probeManager;
    private readonly Awareness _awareness;
    private readonly IndirectPing? _indirectPing;
    private readonly ILogger? _logger;
    
    public SwimProtocol(
        MemberlistConfig config,
        ProbeManager probeManager,
        Awareness awareness,
        ILogger? logger = null)
    {
        _config = config;
        _probeManager = probeManager;
        _awareness = awareness;
        _logger = logger;
    }
    
    public SwimProtocol(
        MemberlistConfig config,
        ProbeManager probeManager,
        Awareness awareness,
        IndirectPing indirectPing,
        ILogger? logger = null)
    {
        _config = config;
        _probeManager = probeManager;
        _awareness = awareness;
        _indirectPing = indirectPing;
        _logger = logger;
    }
    
    /// <summary>
    /// Runs one iteration of the probe cycle.
    /// </summary>
    public async Task<ProbeResult?> ProbeAsync(
        List<NodeState> nodes,
        string localNodeName,
        CancellationToken cancellationToken = default)
    {
        var node = _probeManager.SelectNodeToProbe(nodes, localNodeName);
        if (node == null)
        {
            return null;
        }
        
        _logger?.LogDebug("Probing node {Node}", node.Name);
        
        // Scale timeout based on health
        var timeout = ScaleTimeout(_config.ProbeTimeout);
        
        // Direct probe
        var result = await _probeManager.ProbeNodeAsync(node, timeout, cancellationToken);
        
        if (!result.Success && _config.IndirectChecks > 0)
        {
            // Try indirect probes
            result = await IndirectProbeAsync(nodes, node, localNodeName, timeout, cancellationToken);
        }
        
        return result;
    }
    
    /// <summary>
    /// Performs indirect probing through other nodes.
    /// </summary>
    private async Task<ProbeResult> IndirectProbeAsync(
        List<NodeState> allNodes,
        NodeState target,
        string localNodeName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var indirectNodes = _probeManager.SelectIndirectNodes(
            allNodes, target.Name, localNodeName, _config.IndirectChecks);
        
        _logger?.LogDebug("Indirect probe of {Target} via {Count} nodes", 
            target.Name, indirectNodes.Count);
        
        bool success = false;
        if (_indirectPing != null && indirectNodes.Count > 0)
        {
            // Use IndirectPing to send requests through intermediaries
            success = await _indirectPing.IndirectPingAsync(target, indirectNodes, timeout, cancellationToken);
        }
        
        return new ProbeResult
        {
            Success = success,
            NodeName = target.Name,
            Rtt = timeout,
            UsedTcp = false,
            IndirectChecks = indirectNodes.Count
        };
    }
    
    /// <summary>
    /// Scales timeout based on current health score.
    /// </summary>
    private TimeSpan ScaleTimeout(TimeSpan baseTimeout)
    {
        var score = _awareness.GetHealthScore();
        if (score == 0)
        {
            return baseTimeout;
        }
        
        var multiplier = Math.Min(score + 1, _config.AwarenessMaxMultiplier);
        return TimeSpan.FromTicks(baseTimeout.Ticks * multiplier);
    }
}
