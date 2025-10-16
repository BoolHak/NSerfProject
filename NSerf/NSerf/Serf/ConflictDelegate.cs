// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/conflict_delegate.go

using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Serf;

/// <summary>
/// ConflictDelegate is the Serf implementation of IConflictDelegate from Memberlist.
/// It handles name conflicts when two nodes attempt to join with the same name but different addresses.
/// </summary>
internal class ConflictDelegate : IConflictDelegate
{
    private readonly Serf _serf;

    /// <summary>
    /// Creates a new ConflictDelegate for the given Serf instance.
    /// </summary>
    /// <param name="serf">The Serf instance to forward conflict notifications to</param>
    /// <exception cref="ArgumentNullException">Thrown if serf is null</exception>
    public ConflictDelegate(Serf serf)
    {
        _serf = serf ?? throw new ArgumentNullException(nameof(serf));
    }

    /// <summary>
    /// Invoked when a name conflict is detected - two nodes with the same name but different addresses.
    /// Forwards to Serf's internal node conflict handler.
    /// </summary>
    /// <param name="existing">The existing node in the cluster</param>
    /// <param name="other">The other node attempting to join with the same name</param>
    public void NotifyConflict(Node existing, Node other)
    {
        _serf.HandleNodeConflict(existing, other);
    }
}
