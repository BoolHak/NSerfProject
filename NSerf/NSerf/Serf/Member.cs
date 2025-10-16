// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;

namespace NSerf.Serf;

/// <summary>
/// Represents a member in the Serf cluster.
/// Minimal implementation for Phase 0 - will be expanded in Phase 1.
/// </summary>
public class Member
{
    /// <summary>
    /// Unique name of the member.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the member.
    /// </summary>
    public IPAddress Addr { get; set; } = IPAddress.None;

    /// <summary>
    /// Port the member is listening on.
    /// </summary>
    public ushort Port { get; set; }

    /// <summary>
    /// Tags associated with this member.
    /// </summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>
    /// Current status of the member.
    /// </summary>
    public MemberStatus Status { get; set; }
}
