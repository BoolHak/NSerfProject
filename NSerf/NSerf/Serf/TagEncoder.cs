// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/serf.go

using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// TagEncoder provides encoding and decoding of member tags.
/// Tags are encoded with a magic byte prefix (255) followed by MessagePack-encoded dictionary.
/// Supports backwards compatibility with protocol version 2 which used raw "role" strings.
/// </summary>
public static class TagEncoder
{
    /// <summary>
    /// Magic byte used to identify tag-encoded metadata (protocol version >= 3).
    /// Value 255 is chosen to be unlikely to appear as the first byte of a role string.
    /// </summary>
    public const byte TagMagicByte = 255;

    /// <summary>
    /// Minimum protocol version that supports full tag encoding.
    /// Protocol version 2 and below only support a single "role" tag.
    /// </summary>
    public const int MinTagProtocolVersion = 3;

    /// <summary>
    /// Encodes a tag dictionary into bytes for transmission.
    /// </summary>
    /// <param name="tags">Dictionary of tags to encode</param>
    /// <param name="protocolVersion">Serf protocol version (for backwards compatibility)</param>
    /// <returns>Encoded byte array</returns>
    public static byte[] EncodeTags(Dictionary<string, string> tags, int protocolVersion)
    {
        if (tags == null)
        {
            tags = new Dictionary<string, string>();
        }

        // Support role-only backwards compatibility for protocol version < 3
        if (protocolVersion < MinTagProtocolVersion)
        {
            // Extract role tag and encode as raw string
            if (tags.TryGetValue("role", out var role))
            {
                return System.Text.Encoding.UTF8.GetBytes(role);
            }
            return Array.Empty<byte>();
        }

        // Protocol version >= 3: Use magic byte prefix and MessagePack encoding
        using var ms = new MemoryStream();
        
        // Write magic byte prefix
        ms.WriteByte(TagMagicByte);
        
        // MessagePack encode the tags
        try
        {
            MessagePackSerializer.Serialize(ms, tags, MessagePackSerializerOptions.Standard);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to encode tags: {ex.Message}", ex);
        }

        return ms.ToArray();
    }

    /// <summary>
    /// Decodes tags from encoded bytes.
    /// Handles both new format (magic byte + MessagePack) and legacy format (raw role string).
    /// </summary>
    /// <param name="buffer">Encoded tag data</param>
    /// <returns>Dictionary of decoded tags</returns>
    public static Dictionary<string, string> DecodeTags(byte[] buffer)
    {
        if (buffer == null || buffer.Length == 0)
        {
            // Empty buffer means no tags, but backwards compatibility: treat as empty role
            return new Dictionary<string, string>();
        }

        // Check for magic byte (protocol version >= 3)
        if (buffer[0] != TagMagicByte)
        {
            // Backwards compatibility mode: treat entire buffer as a "role" string
            var role = System.Text.Encoding.UTF8.GetString(buffer);
            return new Dictionary<string, string> { ["role"] = role };
        }

        // Skip magic byte and decode MessagePack
        try
        {
            var tags = MessagePackSerializer.Deserialize<Dictionary<string, string>>(
                buffer.AsMemory(1), 
                MessagePackSerializerOptions.Standard);
            
            return tags ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            // On decode error, return empty tags (logging would happen in Serf class)
            System.Diagnostics.Debug.WriteLine($"Failed to decode tags: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Checks if the buffer contains tag-encoded data (has magic byte prefix).
    /// </summary>
    /// <param name="buffer">Buffer to check</param>
    /// <returns>True if buffer starts with magic byte, false otherwise</returns>
    public static bool IsTagEncoded(byte[] buffer)
    {
        return buffer != null && buffer.Length > 0 && buffer[0] == TagMagicByte;
    }
}
