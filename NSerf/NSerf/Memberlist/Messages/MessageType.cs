// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
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
