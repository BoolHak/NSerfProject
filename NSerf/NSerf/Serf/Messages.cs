// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/messages.go

using System.Net;
using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// MessageType identifies the types of gossip messages Serf will send along memberlist.
/// Values match the Go implementation order.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// Leave message - member is leaving the cluster gracefully.
    /// </summary>
    Leave = 0,

    /// <summary>
    /// Join message - member is joining the cluster.
    /// </summary>
    Join = 1,

    /// <summary>
    /// Push/pull message - state synchronization.
    /// </summary>
    PushPull = 2,

    /// <summary>
    /// User event message - custom application event.
    /// </summary>
    UserEvent = 3,

    /// <summary>
    /// Query message - distributed query request.
    /// </summary>
    Query = 4,

    /// <summary>
    /// Query response message - response to a query.
    /// </summary>
    QueryResponse = 5,

    /// <summary>
    /// Conflict response message - address/name conflict resolution.
    /// </summary>
    ConflictResponse = 6,

    /// <summary>
    /// Key request message - encryption key management.
    /// </summary>
    KeyRequest = 7,

    /// <summary>
    /// Key response message - encryption key response.
    /// </summary>
    KeyResponse = 8,

    /// <summary>
    /// Relay message - message forwarding.
    /// </summary>
    Relay = 9
}

/// <summary>
/// Query flags used in messageQuery to control query behavior.
/// </summary>
[Flags]
public enum QueryFlags : uint
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,

    /// <summary>
    /// Ack flag is used to force receiver to send an ack back.
    /// </summary>
    Ack = 1 << 0,  // 1

    /// <summary>
    /// NoBroadcast is used to prevent re-broadcast of a query.
    /// This can be used to selectively send queries to individual members.
    /// </summary>
    NoBroadcast = 1 << 1  // 2
}

/// <summary>
/// FilterType is used with a query filter to specify the type of filter we are sending.
/// </summary>
public enum FilterType : byte
{
    /// <summary>
    /// Filter by node names.
    /// </summary>
    Node = 0,

    /// <summary>
    /// Filter by tag values using regular expressions.
    /// </summary>
    Tag = 1
}

/// <summary>
/// MessageJoin is the message broadcasted after we join to associate the node with a lamport clock.
/// </summary>
[MessagePackObject]
public class MessageJoin
{
    [Key(0)]
    public LamportTime LTime { get; set; }

    [Key(1)]
    public string Node { get; set; } = string.Empty;
}

/// <summary>
/// MessageLeave is the message broadcasted to signal the intentional leave.
/// </summary>
[MessagePackObject]
public class MessageLeave
{
    [Key(0)]
    public LamportTime LTime { get; set; }

    [Key(1)]
    public string Node { get; set; } = string.Empty;

    [Key(2)]
    public bool Prune { get; set; }
}

/// <summary>
/// MessagePushPull is used when doing a state exchange.
/// This is a relatively large message, but is sent infrequently.
/// </summary>
[MessagePackObject]
public class MessagePushPull
{
    /// <summary>
    /// Current node lamport time.
    /// </summary>
    [Key(0)]
    public LamportTime LTime { get; set; }

    /// <summary>
    /// Maps the node name to its status lamport time.
    /// </summary>
    [Key(1)]
    public Dictionary<string, LamportTime> StatusLTimes { get; set; } = new();

    /// <summary>
    /// List of left node names.
    /// </summary>
    [Key(2)]
    public List<string> LeftMembers { get; set; } = new();

    /// <summary>
    /// Lamport time for event clock.
    /// </summary>
    [Key(3)]
    public LamportTime EventLTime { get; set; }

    /// <summary>
    /// Recent events.
    /// </summary>
    [Key(4)]
    public List<UserEventCollection> Events { get; set; } = new();

    /// <summary>
    /// Lamport time for query clock.
    /// </summary>
    [Key(5)]
    public LamportTime QueryLTime { get; set; }
}

/// <summary>
/// MessageUserEvent is used for user-generated events.
/// </summary>
[MessagePackObject]
public class MessageUserEvent
{
    [Key(0)]
    public LamportTime LTime { get; set; }

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// CC means "Can Coalesce". Zero value is compatible with Serf 0.1.
    /// </summary>
    [Key(3)]
    public bool CC { get; set; }
}

/// <summary>
/// MessageQuery is used for query events.
/// </summary>
[MessagePackObject]
public class MessageQuery
{
    /// <summary>
    /// Event lamport time.
    /// </summary>
    [Key(0)]
    public LamportTime LTime { get; set; }

    /// <summary>
    /// Query ID, randomly generated.
    /// </summary>
    [Key(1)]
    public uint ID { get; set; }

    /// <summary>
    /// Source address, used for a direct reply.
    /// </summary>
    [Key(2)]
    public byte[] Addr { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Source port, used for a direct reply.
    /// </summary>
    [Key(3)]
    public ushort Port { get; set; }

    /// <summary>
    /// Source node name, used for a direct reply.
    /// </summary>
    [Key(4)]
    public string SourceNode { get; set; } = string.Empty;

    /// <summary>
    /// Potential query filters.
    /// </summary>
    [Key(5)]
    public List<byte[]> Filters { get; set; } = new();

    /// <summary>
    /// Used to provide various flags (Ack, NoBroadcast).
    /// </summary>
    [Key(6)]
    public uint Flags { get; set; }

    /// <summary>
    /// Used to set the number of duplicate relayed responses.
    /// </summary>
    [Key(7)]
    public byte RelayFactor { get; set; }

    /// <summary>
    /// Maximum time between delivery and response.
    /// </summary>
    [Key(8)]
    public TimeSpan Timeout { get; set; }

    /// <summary>
    /// Query name.
    /// </summary>
    [Key(9)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Query payload.
    /// </summary>
    [Key(10)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Checks if the ack flag is set.
    /// </summary>
    [IgnoreMember]
    public bool IsAck => (Flags & (uint)QueryFlags.Ack) != 0;

    /// <summary>
    /// Checks if the no broadcast flag is set.
    /// </summary>
    [IgnoreMember]
    public bool IsNoBroadcast => (Flags & (uint)QueryFlags.NoBroadcast) != 0;
}

/// <summary>
/// MessageQueryResponse is used to respond to a query.
/// </summary>
[MessagePackObject]
public class MessageQueryResponse
{
    /// <summary>
    /// Event lamport time.
    /// </summary>
    [Key(0)]
    public LamportTime LTime { get; set; }

    /// <summary>
    /// Query ID.
    /// </summary>
    [Key(1)]
    public uint ID { get; set; }

    /// <summary>
    /// Node name sending the response.
    /// </summary>
    [Key(2)]
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Used to provide various flags.
    /// </summary>
    [Key(3)]
    public uint Flags { get; set; }

    /// <summary>
    /// Optional response payload.
    /// </summary>
    [Key(4)]
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Checks if the ack flag is set.
    /// </summary>
    [IgnoreMember]
    public bool IsAck => (Flags & (uint)QueryFlags.Ack) != 0;
}

/// <summary>
/// FilterNode is used with the FilterType.Node, and is a list of node names.
/// </summary>
public class FilterNode
{
    public List<string> Nodes { get; set; } = new();
}

/// <summary>
/// FilterTag is used with the FilterType.Tag and is a regular expression to apply to a tag.
/// </summary>
[MessagePackObject]
public class FilterTag
{
    [Key(0)]
    public string Tag { get; set; } = string.Empty;

    [Key(1)]
    public string Expr { get; set; } = string.Empty;
}

/// <summary>
/// RelayHeader is used to store the end destination of a relayed message.
/// </summary>
[MessagePackObject]
public class RelayHeader
{
    [Key(0)]
    public IPEndPoint DestAddr { get; set; } = new(IPAddress.None, 0);

    [Key(1)]
    public string DestName { get; set; } = string.Empty;
}

/// <summary>
/// Message encoding and decoding utilities.
/// </summary>
public static class MessageCodec
{
    private static readonly MessagePackSerializerOptions _standardOptions = MessagePackSerializerOptions.Standard;

    /// <summary>
    /// Decodes a message from bytes into the specified type.
    /// </summary>
    public static T DecodeMessage<T>(byte[] buffer)
    {
        return MessagePackSerializer.Deserialize<T>(buffer, _standardOptions);
    }

    /// <summary>
    /// Encodes a message with a type header byte.
    /// </summary>
    public static byte[] EncodeMessage(MessageType messageType, object message)
    {
        using var ms = new MemoryStream();
        
        // Write type header byte
        ms.WriteByte((byte)messageType);
        
        // Serialize message
        MessagePackSerializer.Serialize(ms, message, _standardOptions);
        
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes a relay message by wrapping it with relay header and destination info.
    /// </summary>
    public static byte[] EncodeRelayMessage(MessageType messageType, IPEndPoint destAddr, string nodeName, object message)
    {
        using var ms = new MemoryStream();
        
        // Write relay type header
        ms.WriteByte((byte)MessageType.Relay);
        
        // Serialize relay header
        var header = new RelayHeader
        {
            DestAddr = destAddr,
            DestName = nodeName
        };
        MessagePackSerializer.Serialize(ms, header, _standardOptions);
        
        // Write actual message type
        ms.WriteByte((byte)messageType);
        
        // Serialize actual message
        MessagePackSerializer.Serialize(ms, message, _standardOptions);
        
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes a filter with a type header byte.
    /// </summary>
    public static byte[] EncodeFilter(FilterType filterType, object filter)
    {
        using var ms = new MemoryStream();
        
        // Write filter type header byte
        ms.WriteByte((byte)filterType);
        
        // Serialize filter
        if (filterType == FilterType.Node && filter is List<string> nodes)
        {
            // For node filters, serialize the list directly
            MessagePackSerializer.Serialize(ms, nodes, _standardOptions);
        }
        else
        {
            MessagePackSerializer.Serialize(ms, filter, _standardOptions);
        }
        
        return ms.ToArray();
    }
}
