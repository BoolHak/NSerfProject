// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Memberlist.Messages;

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
