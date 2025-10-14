// Ported from: github.com/hashicorp/memberlist/alive_delegate.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Delegates;

/// <summary>
/// Used to involve a client in processing a node "alive" message.
/// When a node joins, either through UDP gossip or TCP push/pull, we update the state
/// of that node via an alive message. This can be used to filter a node out and prevent
/// it from being considered a peer using application-specific logic.
/// </summary>
public interface IAliveDelegate
{
    /// <summary>
    /// Invoked when a message about a live node is received from the network.
    /// Returning a non-null error prevents the node from being considered a peer.
    /// </summary>
    /// <param name="peer">The peer node that is alive.</param>
    /// <returns>Error message if node should be rejected, null to accept.</returns>
    string? NotifyAlive(Node peer);
}
