// Ported from: github.com/hashicorp/memberlist/merge_delegate.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Delegates;

/// <summary>
/// Used to involve a client in a potential cluster merge operation.
/// When a node does a TCP push/pull (as part of a join), the delegate is involved
/// and allowed to cancel the join based on custom logic. 
/// NOT invoked as part of the push-pull anti-entropy.
/// </summary>
public interface IMergeDelegate
{
    /// <summary>
    /// Invoked when a merge could take place. Provides a list of nodes known by the peer.
    /// If the return value is non-null, the merge is canceled.
    /// </summary>
    /// <param name="peers">List of nodes known by the peer.</param>
    /// <returns>Error message if merge should be canceled, null to allow merge.</returns>
    string? NotifyMerge(IReadOnlyList<Node> peers);
}
