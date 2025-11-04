// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;
using Microsoft.Extensions.Logging;

namespace NSerf.Serf.Helpers;

/// <summary>
/// Provides message encoding and decoding utilities for Serf protocol messages.
/// Handles tag encoding/decoding and general message serialization.
/// </summary>
public static class SerfMessageEncoder
{
    /// <summary>
    /// Encodes a message with the specified type for transmission.
    /// Format: [MessageType byte][MessagePack serialized payload]
    /// </summary>
    /// <param name="messageType">Type of the message</param>
    /// <param name="message">Message object to encode</param>
    /// <param name="logger">Optional logger for error reporting</param>
    /// <returns>Encoded message bytes, or empty array on error</returns>
    public static byte[] EncodeMessage(MessageType messageType, object message, ILogger? logger = null)
    {
        try
        {
            var payload = MessagePackSerializer.Serialize(message);
            var result = new byte[payload.Length + 1];
            result[0] = (byte)messageType;
            Array.Copy(payload, 0, result, 1, payload.Length);
            return result;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[SerfMessageEncoder] Failed to encode message of type {Type}", messageType);
            return [];
        }
    }

    /// <summary>
    /// Encodes tag dictionary into the byte array using protocol-specific encoding.
    /// </summary>
    /// <param name="tags">Dictionary of tags to encode</param>
    /// <param name="protocolVersion">Serf protocol version</param>
    /// <returns>Encoded tags as a byte array</returns>
    public static byte[] EncodeTags(Dictionary<string, string> tags, byte protocolVersion)
    {
        return TagEncoder.EncodeTags(tags, protocolVersion);
    }

    /// <summary>
    /// Decodes tags from byte array to dictionary.
    /// </summary>
    /// <param name="buffer">Encoded tags buffer</param>
    /// <returns>Dictionary of decoded tags</returns>
    public static Dictionary<string, string> DecodeTags(byte[] buffer)
    {
        return TagEncoder.DecodeTags(buffer);
    }

    /// <summary>
    /// Tries to decode a message from raw bytes.
    /// </summary>
    /// <typeparam name="T">Expected message type</typeparam>
    /// <param name="data">Raw message data (including message type byte)</param>
    /// <param name="expectedType">Expected message type</param>
    /// <param name="message">Decoded message if successful</param>
    /// <param name="logger">Optional logger for error reporting</param>
    /// <returns>True if decoding succeeded, false otherwise</returns>
    public static bool TryDecodeMessage<T>(
        byte[] data,
        MessageType expectedType,
        out T? message,
        ILogger? logger = null)
    {
        message = default;

        if (data.Length < 1)
        {
            logger?.LogWarning("[SerfMessageEncoder] Message too short to contain type byte");
            return false;
        }

        var messageType = (MessageType)data[0];
        if (messageType != expectedType)
        {
            logger?.LogWarning("[SerfMessageEncoder] Message type mismatch: expected {Expected}, got {Actual}",
                expectedType, messageType);
            return false;
        }

        try
        {
            message = MessagePackSerializer.Deserialize<T>(data.AsMemory()[1..]);
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[SerfMessageEncoder] Failed to decode message of type {Type}", messageType);
            return false;
        }
    }
}
