// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Manages joining the cluster.
/// </summary>
public class JoinManager
{
    private readonly Memberlist _memberlist;
    private readonly AddressResolver _addressResolver;
    private readonly ILogger? _logger;
    
    public JoinManager(Memberlist memberlist, AddressResolver addressResolver, ILogger? logger = null)
    {
        _memberlist = memberlist;
        _addressResolver = addressResolver;
        _logger = logger;
    }
    
    /// <summary>
    /// Attempts to join the cluster by contacting existing nodes.
    /// </summary>
    public async Task<JoinResult> JoinAsync(
        List<string> existingNodes,
        CancellationToken cancellationToken = default)
    {
        var result = new JoinResult();
        
        foreach (var node in existingNodes)
        {
            try
            {
                _logger?.LogDebug("Attempting to join via {Node}", node);
                
                // Resolve the address
                var addresses = await _addressResolver.ResolveAsync(node, 7946, cancellationToken);
                
                if (addresses.Count == 0)
                {
                    _logger?.LogWarning("Could not resolve address for {Node}", node);
                    result.FailedNodes.Add(node);
                    continue;
                }
                
                // Initiate push-pull sync with the node (this is the join handshake)
                var addr = new Address
                {
                    Addr = $"{addresses[0]}",
                    Name = node
                };
                
                // Perform TCP push-pull state exchange (join=true)
                var stateResult = await _memberlist.SendAndReceiveStateAsync(addr, join: true, cancellationToken);
                
                if (stateResult.RemoteNodes != null && stateResult.RemoteNodes.Count > 0)
                {
                    result.NumJoined++;
                    result.SuccessfulNodes.Add(node);
                    _logger?.LogInformation("Successfully joined via {Node}, received {Count} nodes",
                        node, stateResult.RemoteNodes.Count);
                }
                else
                {
                    _logger?.LogWarning("Push-pull with {Node} returned no remote nodes", node);
                    result.FailedNodes.Add(node);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to join via {Node}", node);
                result.FailedNodes.Add(node);
                result.Errors.Add(ex);
            }
        }
        
        return result;
    }
}
