// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Configuration;
using Microsoft.Extensions.Logging;

namespace NSerf.Serf;

/// <summary>
/// Configuration for a Serf instance.
/// Minimal implementation for Phase 0 - will be expanded in Phase 4.
/// </summary>
public class SerfConfig
{
    /// <summary>
    /// The name of this node. This must be unique in the cluster.
    /// </summary>
    public string NodeName { get; set; } = string.Empty;

    /// <summary>
    /// Memberlist configuration for the underlying gossip protocol.
    /// </summary>
    public MemberlistConfig MemberlistConfig { get; set; } = new();

    /// <summary>
    /// Logger for Serf operations.
    /// </summary>
    public ILogger? Logger { get; set; }

    // Timing configurations (minimal for Phase 0)
    public TimeSpan ReapInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan ReconnectInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan TombstoneTimeout { get; set; } = TimeSpan.FromHours(24);
}
