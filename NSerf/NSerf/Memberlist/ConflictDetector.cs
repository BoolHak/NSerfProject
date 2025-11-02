// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Detects and handles conflicts between nodes.
/// </summary>
public class ConflictDetector(IConflictDelegate? conflictDelegate, ILogger? logger = null)
{
    private readonly IConflictDelegate? _conflictDelegate = conflictDelegate;
    private readonly ILogger? _logger = logger;

    /// <summary>
    /// Checks for address conflicts between nodes.
    /// </summary>
    public bool DetectConflict(NodeState existing, IPAddress newAddr, ushort newPort)
    {
        if (existing.Node.Addr.Equals(newAddr) && existing.Node.Port == newPort)
        {
            return false;
        }

        _logger?.LogError(
            "Conflicting address for {Node}. Mine: {OldAddr}:{OldPort} Theirs: {NewAddr}:{NewPort} State: {State}",
            existing.Name, existing.Node.Addr, existing.Node.Port, newAddr, newPort, existing.State);

        return true;
    }

    /// <summary>
    /// Notifies delegate of conflict.
    /// </summary>
    public void NotifyConflict(Node existing, Node other)
    {
        _conflictDelegate?.NotifyConflict(existing, other);
    }
}
