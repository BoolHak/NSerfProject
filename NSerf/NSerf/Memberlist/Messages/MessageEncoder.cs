// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using MessagePack;

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Provides encoding and decoding functionality for memberlist protocol messages using MessagePack.
/// </summary>
public static class MessageEncoder
{
    private const int MaxMessagesPerCompound = 255;
    
    /// <summary>
    /// Encodes a message object to bytes with a message type prefix.
    /// </summary>
    /// <typeparam name="T">Type of message to encode.</typeparam>
    /// <param name="messageType">Message type identifier.</param>
    /// <param name="message">Message object to encode.</param>
    /// <returns>Encoded byte array with message type prefix.</returns>
    public static byte[] Encode<T>(MessageType messageType, T message)
    {
        // Serialize the message using MessagePack
        var messageBytes = MessagePackSerializer.Serialize(message);
        
        // Prepend the message type byte
        var result = new byte[1 + messageBytes.Length];
        result[0] = (byte)messageType;
        Buffer.BlockCopy(messageBytes, 0, result, 1, messageBytes.Length);
        
        return result;
    }
    
    /// <summary>
    /// Decodes a MessagePack-encoded byte span into a message object.
    /// The span should NOT include the message type prefix.
    /// </summary>
    /// <typeparam name="T">Type of message to decode.</typeparam>
    /// <param name="buffer">Buffer containing the encoded message (without type prefix).</param>
    /// <returns>Decoded message object.</returns>
    public static T Decode<T>(ReadOnlySpan<byte> buffer)
    {
        return MessagePackSerializer.Deserialize<T>(buffer.ToArray());
    }
    
    /// <summary>
    /// Takes a list of messages and packs them into one or multiple compound messages
    /// based on the limitation of 255 messages per compound.
    /// </summary>
    /// <param name="messages">List of encoded messages.</param>
    /// <returns>List of compound message buffers.</returns>
    public static List<byte[]> MakeCompoundMessages(IReadOnlyList<byte[]> messages)
    {
        var compounds = new List<byte[]>();
        var remainingMessages = messages.ToList();
        
        while (remainingMessages.Count > 0)
        {
            var batchSize = Math.Min(MaxMessagesPerCompound, remainingMessages.Count);
            var batch = remainingMessages.Take(batchSize).ToArray();
            compounds.Add(MakeCompoundMessage(batch));
            remainingMessages = remainingMessages.Skip(batchSize).ToList();
        }
        
        return compounds;
    }
    
    /// <summary>
    /// Creates a single compound message containing multiple sub-messages.
    /// Format: [CompoundMsg byte][num messages:byte][length1:ushort][length2:ushort]...[msg1][msg2]...
    /// </summary>
    /// <param name="messages">Array of encoded messages to bundle.</param>
    /// <returns>Compound message buffer.</returns>
    public static byte[] MakeCompoundMessage(byte[][] messages)
    {
        if (messages.Length > MaxMessagesPerCompound)
        {
            throw new ArgumentException($"Cannot create compound message with more than {MaxMessagesPerCompound} messages", nameof(messages));
        }
        
        // Calculate total size
        var totalSize = 1 + // compound type byte
                       1 + // number of messages
                       messages.Length * 2 + // length prefixes (ushort per message)
                       messages.Sum(m => m.Length); // actual messages
        
        var result = new byte[totalSize];
        var offset = 0;
        
        // Write compound message type
        result[offset++] = (byte)MessageType.Compound;
        
        // Write number of messages
        result[offset++] = (byte)messages.Length;
        
        // Write all message lengths
        foreach (var msg in messages)
        {
            var length = (ushort)msg.Length;
            result[offset++] = (byte)(length >> 8); // Big endian
            result[offset++] = (byte)(length & 0xFF);
        }
        
        // Write all messages
        foreach (var msg in messages)
        {
            Buffer.BlockCopy(msg, 0, result, offset, msg.Length);
            offset += msg.Length;
        }
        
        return result;
    }
    
    /// <summary>
    /// Decodes a compound message and returns the individual message parts.
    /// The input buffer should NOT include the compound type byte.
    /// </summary>
    /// <param name="buffer">Buffer containing compound message data (without type byte).</param>
    /// <returns>Tuple of (truncated count, list of message parts).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the buffer format is invalid.</exception>
    public static (int Truncated, List<byte[]> Parts) DecodeCompoundMessage(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 1)
        {
            throw new InvalidOperationException("missing compound length byte");
        }
        
        var numParts = buffer[0];
        buffer = buffer.Slice(1);
        
        // Check we have enough bytes for all length prefixes
        if (buffer.Length < numParts * 2)
        {
            throw new InvalidOperationException("truncated len slice");
        }
        
        // Read all message lengths
        var lengths = new ushort[numParts];
        for (int i = 0; i < numParts; i++)
        {
            lengths[i] = (ushort)((buffer[i * 2] << 8) | buffer[i * 2 + 1]);
        }
        buffer = buffer.Slice(numParts * 2);
        
        // Extract each message
        var parts = new List<byte[]>();
        var truncated = 0;
        
        for (int i = 0; i < lengths.Length; i++)
        {
            var msgLen = lengths[i];
            
            if (buffer.Length < msgLen)
            {
                // Not enough data for this message and remaining ones
                truncated = numParts - i;
                break;
            }
            
            var message = buffer.Slice(0, msgLen).ToArray();
            parts.Add(message);
            buffer = buffer.Slice(msgLen);
        }
        
        return (truncated, parts);
    }
}
