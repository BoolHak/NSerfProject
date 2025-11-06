// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Agent;

/// <summary>
/// Configuration for mDNS discovery.
/// Maps to: Go's MDNSConfig in config.go
/// </summary>
public class MdnsConfig
{
    /// <summary>
    /// Network interface to use for mDNS.
    /// If not set, the main Interface from AgentConfig will be used.
    /// </summary>
    public string? Interface { get; set; }

    /// <summary>
    /// Disable IPv4 for mDNS discovery.
    /// </summary>
    public bool DisableIPv4 { get; set; }

    /// <summary>
    /// Disable IPv6 for mDNS discovery.
    /// </summary>
    public bool DisableIPv6 { get; set; }
}
