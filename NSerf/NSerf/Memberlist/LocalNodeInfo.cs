// Ported from: github.com/hashicorp/memberlist
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist;

/// <summary>
/// Information about the local node.
/// </summary>
public class LocalNodeInfo
{
    public Node Node { get; set; } = new();
    public IPAddress AdvertiseAddr { get; set; } = IPAddress.None;
    public ushort AdvertisePort { get; set; }
    
    private uint _incarnation;
    
    /// <summary>
    /// Gets or sets the incarnation number.
    /// </summary>
    public uint Incarnation
    {
        get => _incarnation;
        set => _incarnation = value;
    }
    
    /// <summary>
    /// Increments the incarnation number.
    /// </summary>
    public uint IncrementIncarnation()
    {
        return Interlocked.Increment(ref _incarnation);
    }
}
