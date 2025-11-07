// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Suspect broadcast when we suspect a node is dead.
/// </summary>
public class Suspect
{
    public uint Incarnation { get; set; }
    public string Node { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

/// <summary>
/// Alive broadcast when we know a node is alive (also used for joins).
/// </summary>
public class Alive
{
    public uint Incarnation { get; set; }
    public string Node { get; set; } = string.Empty;
    public byte[] Addr { get; set; } = [];
    public ushort Port { get; set; }
    public byte[]? Meta { get; set; }
    public byte[]? Vsn { get; set; } // pmin, pmax, pcur, dmin, dmax, dcur
}

/// <summary>
/// Dead broadcast when we confirm a node is dead (also used for leaves).
/// </summary>
public class Dead
{
    public uint Incarnation { get; set; }
    public string Node { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}
