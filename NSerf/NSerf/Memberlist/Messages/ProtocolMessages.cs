// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;
using NSerf.Memberlist.State;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Ping request sent directly to a node.
/// </summary>
[MessagePackObject]
public class PingMessage
{
    /// <summary>
    /// Sequence number for tracking the ping/ack pair.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }

    /// <summary>
    /// Node name - sent so the target can verify they are the intended recipient.
    /// This protects against an agent restart with a new name.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Source address, used for a direct reply (optional).
    /// </summary>
    [Key(2)]
    public byte[] SourceAddr { get; set; } = [];

    /// <summary>
    /// Source port, used for a direct reply (optional).
    /// </summary>
    [Key(3)]
    public ushort SourcePort { get; set; }

    /// <summary>
    /// Source node name, used for a direct reply (optional).
    /// </summary>
    [Key(4)]
    public string SourceNode { get; set; } = string.Empty;
}

/// <summary>
/// Indirect ping sent to an indirect node.
/// </summary>
[MessagePackObject]
public class IndirectPingMessage
{
    /// <summary>
    /// Sequence number for tracking.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }

    /// <summary>
    /// Target node address.
    /// </summary>
    [Key(1)]
    public byte[] Target { get; set; } = [];

    /// <summary>
    /// Target node port.
    /// </summary>
    [Key(2)]
    public ushort Port { get; set; }

    /// <summary>
    /// Target node name - for verification.
    /// </summary>
    [Key(3)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// True if we'd like a nack back.
    /// </summary>
    [Key(4)]
    public bool Nack { get; set; }

    /// <summary>
    /// Source address, used for a direct reply (optional).
    /// </summary>
    [Key(5)]
    public byte[] SourceAddr { get; set; } = [];

    /// <summary>
    /// Source port, used for a direct reply (optional).
    /// </summary>
    [Key(6)]
    public ushort SourcePort { get; set; }

    /// <summary>
    /// Source node name, used for a direct reply (optional).
    /// </summary>
    [Key(7)]
    public string SourceNode { get; set; } = string.Empty;
}

/// <summary>
/// Acknowledgment response sent for a ping.
/// </summary>
[MessagePackObject]
public class AckRespMessage
{
    /// <summary>
    /// Sequence number matching the ping.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }

    /// <summary>
    /// Optional payload data.
    /// </summary>
    [Key(1)]
    public byte[] Payload { get; set; } = [];
}

/// <summary>
/// Negative acknowledgment response sent for an indirect ping when the pinger doesn't 
/// hear from the ping-ee within the configured timeout.
/// </summary>
[MessagePackObject]
public class NackRespMessage
{
    /// <summary>
    /// Sequence number matching the ping.
    /// </summary>
    [Key(0)]
    public uint SeqNo { get; set; }
}

/// <summary>
/// Error response sent to relay the error from the remote end.
/// </summary>
[MessagePackObject]
public class ErrRespMessage
{
    /// <summary>
    /// Error message text.
    /// </summary>
    [Key(0)]
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Suspect message is broadcast when we suspect a node is dead.
/// </summary>
[MessagePackObject]
public class SuspectMessage
{
    /// <summary>
    /// Incarnation number of the suspected node.
    /// </summary>
    [Key(0)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Name of the suspected node.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Name of the node reporting the suspicion.
    /// </summary>
    [Key(2)]
    public string From { get; set; } = string.Empty;
}

/// <summary>
/// Alive message is broadcast when we know a node is alive.
/// Also used for nodes joining the cluster.
/// </summary>
[MessagePackObject]
public class AliveMessage
{
    /// <summary>
    /// Incarnation number of the alive node.
    /// </summary>
    [Key(0)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Name of the alive node.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the node.
    /// </summary>
    [Key(2)]
    public byte[] Addr { get; set; } = [];

    /// <summary>
    /// Port number of the node.
    /// </summary>
    [Key(3)]
    public ushort Port { get; set; }

    /// <summary>
    /// Metadata from the delegate for this node.
    /// </summary>
    [Key(4)]
    public byte[] Meta { get; set; } = [];

    /// <summary>
    /// Protocol versions: [pmin, pmax, pcur, dmin, dmax, dcur].
    /// </summary>
    [Key(5)]
    public byte[] Vsn { get; set; } = [];
}

/// <summary>
/// Dead message is broadcast when we confirm a node is dead.
/// Also used for nodes leaving the cluster.
/// </summary>
[MessagePackObject]
public class DeadMessage
{
    /// <summary>
    /// Incarnation number of the dead node.
    /// </summary>
    [Key(0)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Name of the dead node.
    /// </summary>
    [Key(1)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// Name of the node reporting the death.
    /// </summary>
    [Key(2)]
    public string From { get; set; } = string.Empty;
}

/// <summary>
/// Push/pull header used to inform the other side how many states we are transferring.
/// </summary>
[MessagePackObject]
public class PushPullHeader
{
    /// <summary>
    /// Number of node states being transferred.
    /// </summary>
    [Key(0)]
    public int Nodes { get; set; }

    /// <summary>
    /// Length of user state in bytes.
    /// </summary>
    [Key(1)]
    public int UserStateLen { get; set; }

    /// <summary>
    /// True if this is a join request, false for anti-entropy.
    /// </summary>
    [Key(2)]
    public bool Join { get; set; }
}

/// <summary>
/// User message header used to encapsulate a user message.
/// </summary>
[MessagePackObject]
public class UserMsgHeader
{
    /// <summary>
    /// Length of user message in bytes.
    /// </summary>
    [Key(0)]
    public int UserMsgLen { get; set; }
}

/// <summary>
/// Push node state used for push/pull requests when transferring node states.
/// </summary>
[MessagePackObject]
public class PushNodeState
{
    /// <summary>
    /// Node name.
    /// </summary>
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IP address.
    /// </summary>
    [Key(1)]
    public byte[] Addr { get; set; } = [];

    /// <summary>
    /// Port number.
    /// </summary>
    [Key(2)]
    public ushort Port { get; set; }

    /// <summary>
    /// Node metadata.
    /// </summary>
    [Key(3)]
    public byte[] Meta { get; set; } = [];

    /// <summary>
    /// Incarnation number.
    /// </summary>
    [Key(4)]
    public uint Incarnation { get; set; }

    /// <summary>
    /// Current state of the node.
    /// </summary>
    [Key(5)]
    public NodeStateType State { get; set; }

    /// <summary>
    /// Protocol versions.
    /// </summary>
    [Key(6)]
    public byte[] Vsn { get; set; } = [];
}

/// <summary>
/// Compress wrapper used to wrap an underlying payload using a specified compression algorithm.
/// </summary>
[MessagePackObject]
public class CompressMessage
{
    /// <summary>
    /// Compression algorithm used.
    /// </summary>
    [Key(0)]
    public CompressionType Algo { get; set; }

    /// <summary>
    /// Compressed payload.
    /// </summary>
    [Key(1)]
    public byte[] Buf { get; set; } = [];
}
