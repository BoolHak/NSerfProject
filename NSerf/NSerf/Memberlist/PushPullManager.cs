// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Manages push/pull state synchronization.
/// </summary>
public class PushPullManager
{
    private readonly ILogger? _logger;
    private int _activePushPullRequests;
    
    public PushPullManager(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Gets the current number of active push/pull requests.
    /// </summary>
    public int ActiveRequests => Interlocked.CompareExchange(ref _activePushPullRequests, 0, 0);
    
    /// <summary>
    /// Increments the active request counter.
    /// </summary>
    public void BeginRequest()
    {
        Interlocked.Increment(ref _activePushPullRequests);
    }
    
    /// <summary>
    /// Decrements the active request counter.
    /// </summary>
    public void EndRequest()
    {
        Interlocked.Decrement(ref _activePushPullRequests);
    }
    
    /// <summary>
    /// Checks if we should accept a new push/pull request.
    /// </summary>
    public bool CanAcceptRequest(int maxConcurrent = 128)
    {
        return ActiveRequests < maxConcurrent;
    }
    
    /// <summary>
    /// Converts local node states to push format.
    /// </summary>
    public List<PushNodeState> SerializeNodeStates(List<NodeState> nodes)
    {
        return nodes.Select(n => new PushNodeState
        {
            Name = n.Name,
            Addr = n.Node.Addr.GetAddressBytes(),
            Port = n.Node.Port,
            Meta = n.Node.Meta,
            Incarnation = n.Incarnation,
            State = n.State,
            Vsn = new[] { n.Node.PMin, n.Node.PMax, n.Node.PCur, n.Node.DMin, n.Node.DMax, n.Node.DCur }
        }).ToList();
    }
}
