// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Manages to join the cluster.
/// </summary>
public class JoinManager(Memberlist memberlist, AddressResolver addressResolver, ILogger? logger = null)
{
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
                logger?.LogDebug("Attempting to join via {Node}", node);

                // Parse node format: "NodeName/IP:Port" or "IP:Port"
                var nodeName = "";
                var addressToParse = node;

                if (node.Contains('/'))
                {
                    var parts = node.Split('/', 2);
                    nodeName = parts[0];
                    addressToParse = parts[1];
                }

                // Resolve the address
                var addresses = await addressResolver.ResolveAsync(addressToParse, 7946, cancellationToken);

                if (addresses.Count == 0)
                {
                    logger?.LogWarning("Could not resolve address for {Node}", node);
                    result.FailedNodes.Add(node);
                    continue;
                }

                // Initiate push-pull sync with the node (this is the join handshake)
                var addr = new Address
                {
                    Addr = $"{addresses[0].Address}:{addresses[0].Port}",
                    Name = nodeName
                };

                // Perform TCP push-pull state exchange (join=true)
                var (remoteNodes, _) = await memberlist.SendAndReceiveStateAsync(addr, join: true, cancellationToken);

                if (remoteNodes is { Count: > 0 })
                {
                    result.NumJoined++;
                    result.SuccessfulNodes.Add(node);
                    logger?.LogInformation("Successfully joined via {Node}, received {Count} nodes",
                        node, remoteNodes.Count);
                }
                else
                {
                    logger?.LogWarning("Push-pull with {Node} returned no remote nodes", node);
                    result.FailedNodes.Add(node);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to join via {Node}", node);
                result.FailedNodes.Add(node);
                result.Errors.Add(ex);
            }
        }

        return result;
    }
}
