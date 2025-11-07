// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist;

/// <summary>
/// Protocol version constants for the memberlist.
/// </summary>
public static class ProtocolVersion
{
    /// <summary>
    /// Minimum protocol version that we can understand.
    /// </summary>
    public const byte Min = 1;
    
    /// <summary>
    /// Version 3 added support for TCP pings, but we kept the default
    /// protocol version at 2 to ease the transition to this new feature.
    /// </summary>
    public const byte Version2Compatible = 2;
    
    /// <summary>
    /// Maximum protocol version that we can understand.
    /// </summary>
    public const byte Max = 5;
}
