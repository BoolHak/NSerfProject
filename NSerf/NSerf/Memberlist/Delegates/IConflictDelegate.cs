// Ported from: github.com/hashicorp/memberlist/conflict_delegate.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Delegates;

/// <summary>
/// Used to inform a client that a node has attempted to join which would result in a name conflict.
/// This happens if two clients are configured with the same name but different addresses.
/// </summary>
public interface IConflictDelegate
{
    /// <summary>
    /// Invoked when a name conflict is detected.
    /// </summary>
    /// <param name="existing">The existing node in the cluster.</param>
    /// <param name="other">The other node attempting to join with the same name.</param>
    void NotifyConflict(Node existing, Node other);
}
