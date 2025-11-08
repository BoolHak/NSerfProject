// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Constants for memberlist message handling.
/// </summary>
public static class MessageConstants
{
    /// <summary>
    /// Maximum size for node metadata.
    /// </summary>
    public const int MetaMaxSize = 512;
    
    /// <summary>
    /// Assumed header overhead for compound messages.
    /// </summary>
    public const int CompoundHeaderOverhead = 2;
    
    /// <summary>
    /// Assumed overhead per entry in the compound header.
    /// </summary>
    public const int CompoundOverhead = 2;
    
    /// <summary>
    /// User message overhead.
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
    /// UDP packet buffer size.
    /// </summary>
    public const int UdpPacketBufSize = 65536;
    
    /// <summary>
    /// UDP receive buffer size (target).
    /// </summary>
    public const int UdpRecvBufSize = 2 * 1024 * 1024;
}
