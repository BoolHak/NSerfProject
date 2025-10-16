// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

namespace NSerf.Serf;

/// <summary>
/// QueryCollection stores all query IDs at a specific Lamport time.
/// Used for query tracking and de-duplication to prevent processing
/// the same query multiple times.
/// </summary>
internal class QueryCollection
{
    /// <summary>
    /// Lamport time when these queries occurred.
    /// </summary>
    public LamportTime LTime { get; set; }

    /// <summary>
    /// List of query IDs at this lamport time.
    /// </summary>
    public List<uint> QueryIDs { get; set; } = new();

    /// <summary>
    /// Returns a string representation of the query collection.
    /// </summary>
    public override string ToString()
    {
        return $"Queries at LTime {LTime}: {QueryIDs.Count} query(ies)";
    }
}
