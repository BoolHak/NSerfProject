// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.IO.Compression;
using NSerf.Memberlist.Messages;

namespace NSerf.Memberlist.Common;

/// <summary>
/// Utilities for compressing and decompressing message payloads.
/// </summary>
public static class CompressionUtils
{
    /// <summary>
    /// Compresses an input buffer using GZip compression.
    /// </summary>
    /// <param name="input">Data to compress.</param>
    /// <returns>Compressed data.</returns>
    public static byte[] CompressPayload(byte[] input)
    {
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Fastest))
        {
            gzipStream.Write(input, 0, input.Length);
        }
        return outputStream.ToArray();
    }
    
    /// <summary>
    /// Decompresses a GZip compressed payload.
    /// </summary>
    /// <param name="compressedMessage">Compressed message bytes.</param>
    /// <returns>Decompressed data.</returns>
    public static byte[] DecompressPayload(ReadOnlySpan<byte> compressedMessage)
    {
        using var inputStream = new MemoryStream(compressedMessage.ToArray());
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
