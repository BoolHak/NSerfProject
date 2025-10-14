// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Message type identifier for messages that can be received on network channels from other members.
/// WARNING: ONLY APPEND TO THIS LIST! The numeric values are part of the protocol itself.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// Ping request sent directly to a node.
    /// </summary>
    Ping = 0,
    
    /// <summary>
    /// Indirect ping sent to an indirect node.
    /// </summary>
    IndirectPing = 1,
    
    /// <summary>
    /// Acknowledgment response for a ping.
    /// </summary>
    AckResp = 2,
    
    /// <summary>
    /// Broadcast when we suspect a node is dead.
    /// </summary>
    Suspect = 3,
    
    /// <summary>
    /// Broadcast when we know a node is alive (also used for joins).
    /// </summary>
    Alive = 4,
    
    /// <summary>
    /// Broadcast when we confirm a node is dead (also used for leaves).
    /// </summary>
    Dead = 5,
    
    /// <summary>
    /// Push/pull synchronization message.
    /// </summary>
    PushPull = 6,
    
    /// <summary>
    /// Compound message containing multiple sub-messages.
    /// </summary>
    Compound = 7,
    
    /// <summary>
    /// User message, not handled by memberlist internally.
    /// </summary>
    User = 8,
    
    /// <summary>
    /// Compressed message wrapper.
    /// </summary>
    Compress = 9,
    
    /// <summary>
    /// Encrypted message wrapper.
    /// </summary>
    Encrypt = 10,
    
    /// <summary>
    /// Negative acknowledgment response.
    /// </summary>
    NackResp = 11,
    
    /// <summary>
    /// Message with CRC32 checksum.
    /// </summary>
    HasCrc = 12,
    
    /// <summary>
    /// Error response message.
    /// </summary>
    Err = 13,
    
    /// <summary>
    /// Message with label prefix (deliberately high value to disambiguate from encryption version).
    /// </summary>
    HasLabel = 244
}

/// <summary>
/// Compression algorithm identifier.
/// </summary>
public enum CompressionType : byte
{
    /// <summary>
    /// LZW compression algorithm.
    /// </summary>
    Lzw = 0
}

/// <summary>
/// Constants for message processing.
/// </summary>
public static class MessageConstants
{
    /// <summary>
    /// Maximum size for node metadata in bytes.
    /// </summary>
    public const int MetaMaxSize = 512;
    
    /// <summary>
    /// Assumed header overhead for compound messages.
    /// </summary>
    public const int CompoundHeaderOverhead = 2;
    
    /// <summary>
    /// Assumed overhead per entry in compound message.
    /// </summary>
    public const int CompoundOverhead = 2;
    
    /// <summary>
    /// Overhead for user messages.
    /// </summary>
    public const int UserMsgOverhead = 1;
    
    /// <summary>
    /// Warning threshold for UDP packet processing time.
    /// </summary>
    public static readonly TimeSpan BlockingWarning = TimeSpan.FromMilliseconds(10);
    
    /// <summary>
    /// Maximum bytes for push state.
    /// </summary>
    public const int MaxPushStateBytes = 20 * 1024 * 1024;
    
    /// <summary>
    /// Maximum number of concurrent push/pull requests.
    /// </summary>
    public const int MaxPushPullRequests = 128;
    
    /// <summary>
    /// LZW literal width for compression.
    /// </summary>
    public const int LzwLitWidth = 8;
    
    /// <summary>
    /// Minimum protocol version supported.
    /// </summary>
    public const byte ProtocolVersionMin = 1;
    
    /// <summary>
    /// Protocol version 2 compatible mode.
    /// </summary>
    public const byte ProtocolVersion2Compatible = 2;
    
    /// <summary>
    /// Maximum protocol version supported.
    /// </summary>
    public const byte ProtocolVersionMax = 5;
}
