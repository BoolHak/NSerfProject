// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages the complete lifecycle of nodes in the cluster.
/// </summary>
public class NodeLifecycleManager(ILogger? logger = null)
{
    private readonly Dictionary<string, NodeState> _nodeMap = [];
    private readonly List<NodeState> _nodes = [];
    private readonly object _lock = new();
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Adds or updates a node.
    /// </summary>
    public void AddOrUpdateNode(NodeState node)
    {
        lock (_lock)
        {
            if (_nodeMap.TryGetValue(node.Name, out var existing))
            {
                var idx = _nodes.IndexOf(existing);
                if (idx >= 0)
                {
                    _nodes[idx] = node;
                }
                _nodeMap[node.Name] = node;
            }
            else
            {
                _nodes.Add(node);
                _nodeMap[node.Name] = node;
                _logger?.LogInformation("Added new node {Node}", node.Name);
            }
        }
    }

    /// <summary>
    /// Gets a node by name.
    /// </summary>
    public NodeState? GetNode(string name)
    {
        lock (_lock)
        {
            return _nodeMap.TryGetValue(name, out var node) ? node : null;
        }
    }

    /// <summary>
    /// Gets all nodes.
    /// </summary>
    public List<NodeState> GetAllNodes()
    {
        lock (_lock)
        {
            return [.. _nodes];
        }
    }

    /// <summary>
    /// Gets alive nodes.
    /// </summary>
    public List<NodeState> GetAliveNodes()
    {
        lock (_lock)
        {
            return [.. _nodes.Where(n => n.State == NodeStateType.Alive)];
        }
    }

    /// <summary>
    /// Removes a node.
    /// </summary>
    public bool RemoveNode(string name)
    {
        lock (_lock)
        {
            if (_nodeMap.Remove(name, out var node))
            {
                _nodes.Remove(node);
                _logger?.LogInformation("Removed node {Node}", name);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets the count of nodes.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _nodes.Count;
            }
        }
    }
}
