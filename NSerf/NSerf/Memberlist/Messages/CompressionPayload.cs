// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Common;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Handles payload compression and decompression for messages.
/// NOTE: This is now handled by CompressionUtils in Common namespace.
/// This class exists for API compatibility.
/// </summary>
public static class CompressionPayload
{
    /// <summary>
    /// Takes an input buffer, compresses it using LZW, and wraps it in a compress message.
    /// </summary>
    public static byte[] CompressPayload(byte[] input)
    {
        return CompressionUtils.CompressPayload(input);
    }
    
    /// <summary>
    /// Unpacks an encoded compress message and returns its payload decompressed.
    /// </summary>
    public static byte[] DecompressPayload(byte[] msg)
    {
        return CompressionUtils.DecompressPayload(msg);
    }
}
