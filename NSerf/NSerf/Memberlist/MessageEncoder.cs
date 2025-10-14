// Ported from: github.com/hashicorp/memberlist
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using NSerf.Memberlist.Messages;

namespace NSerf.Memberlist;

/// <summary>
/// Encodes and decodes protocol messages.
/// </summary>
public static class MessageEncoder
{
    /// <summary>
    /// Encodes a message with its type prefix.
    /// </summary>
    public static byte[] EncodeMessage(MessageType type, byte[] payload)
    {
        var result = new byte[1 + payload.Length];
        result[0] = (byte)type;
        Array.Copy(payload, 0, result, 1, payload.Length);
        return result;
    }
    
    /// <summary>
    /// Decodes a message type from a buffer.
    /// </summary>
    public static (MessageType Type, byte[] Payload) DecodeMessage(byte[] buffer)
    {
        if (buffer.Length < 1)
        {
            throw new ArgumentException("Buffer too short");
        }
        
        var type = (MessageType)buffer[0];
        var payload = new byte[buffer.Length - 1];
        Array.Copy(buffer, 1, payload, 0, payload.Length);
        
        return (type, payload);
    }
}
