// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Ping request sent directly to node.
/// </summary>
public class Ping
{
    public uint SeqNo { get; set; }
    public string Node { get; set; } = string.Empty;
    public byte[]? SourceAddr { get; set; }
    public ushort SourcePort { get; set; }
    public string? SourceNode { get; set; }
}

/// <summary>
/// Indirect ping sent to an indirect node.
/// </summary>
public class IndirectPingReq
{
    public uint SeqNo { get; set; }
    public byte[] Target { get; set; } = [];
    public ushort Port { get; set; }
    public string Node { get; set; } = string.Empty;
    public bool Nack { get; set; }
    public byte[]? SourceAddr { get; set; }
    public ushort SourcePort { get; set; }
    public string? SourceNode { get; set; }
}

/// <summary>
/// Ack response sent for a ping.
/// </summary>
public class AckResp
{
    public uint SeqNo { get; set; }
    public byte[]? Payload { get; set; }
}

/// <summary>
/// Nack response sent for an indirect ping when timeout occurs.
/// </summary>
public class NackResp
{
    public uint SeqNo { get; set; }
}

/// <summary>
/// Error response to relay error from remote end.
/// </summary>
public class ErrResp
{
    public string Error { get; set; } = string.Empty;
}

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


/// <summary>
/// Message handoff for transferring messages between goroutines.
/// </summary>
internal class MsgHandoff
{
    public MessageType MsgType { get; set; }
    public byte[] Buf { get; set; } = [];
    public System.Net.EndPoint? From { get; set; }
}

/// <summary>
/// Ack message for internal use.
/// </summary>
internal class AckMessage
{
    public bool Complete { get; set; }
    public byte[]? Payload { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
