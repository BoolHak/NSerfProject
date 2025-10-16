// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Serf;

/// <summary>
/// Main Serf class for cluster membership and coordination.
/// Minimal stub for Phase 0 - will be implemented in Phase 9.
/// </summary>
public class Serf : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Returns the number of members known to this Serf instance.
    /// Stub implementation for Phase 0.
    /// </summary>
    public int NumMembers()
    {
        // Stub - always returns 1 for Phase 0
        return 1;
    }

    /// <summary>
    /// Disposes the Serf instance.
    /// </summary>
    public void Dispose()
    {
        // Stub for Phase 0
    }

    /// <summary>
    /// Asynchronously disposes the Serf instance.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        // Stub for Phase 0
        return ValueTask.CompletedTask;
    }
}
