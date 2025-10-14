// Ported from: github.com/hashicorp/memberlist/util.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

namespace NSerf.Memberlist.Messages;

/// <summary>
/// Handles compound message creation and parsing.
/// </summary>
public static class CompoundMessage
{
    private const int MaxMessagesPerCompound = 255;
    
    /// <summary>
    /// Takes a list of messages and packs them into one or multiple compound messages.
    /// Each compound message can contain up to 255 messages.
    /// </summary>
    public static List<byte[]> MakeCompoundMessages(List<byte[]> msgs)
    {
        var results = new List<byte[]>();
        int offset = 0;
        
        while (offset < msgs.Count)
        {
            int count = Math.Min(MaxMessagesPerCompound, msgs.Count - offset);
            var batch = msgs.GetRange(offset, count);
            results.Add(MakeCompoundMessage(batch));
            offset += count;
        }
        
        return results;
    }
    
    /// <summary>
    /// Takes a list of messages and generates a single compound message containing all of them.
    /// Format: [compoundMsg byte][count byte][length1 uint16][length2 uint16]...[msg1][msg2]...
    /// </summary>
    public static byte[] MakeCompoundMessage(List<byte[]> msgs)
    {
        if (msgs.Count > MaxMessagesPerCompound)
        {
            throw new ArgumentException($"Cannot create compound message with more than {MaxMessagesPerCompound} messages");
        }
        
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        // Write message type
        writer.Write((byte)MessageType.Compound);
        
        // Write number of messages
        writer.Write((byte)msgs.Count);
        
        // Write message lengths (big-endian uint16)
        foreach (var msg in msgs)
        {
            ushort length = (ushort)msg.Length;
            writer.Write((byte)(length >> 8));
            writer.Write((byte)(length & 0xFF));
        }
        
        // Write message contents
        foreach (var msg in msgs)
        {
            writer.Write(msg);
        }
        
        return ms.ToArray();
    }
    
    /// <summary>
    /// Splits a compound message and returns the slices of individual messages.
    /// Returns the number of truncated messages and the parts.
    /// </summary>
    public static (int Truncated, List<byte[]> Parts) DecodeCompoundMessage(byte[] buf)
    {
        if (buf.Length < 1)
        {
            throw new ArgumentException("Missing compound length byte");
        }
        
        int numParts = buf[0];
        int offset = 1;
        
        // Check we have enough bytes for lengths
        if (buf.Length < offset + numParts * 2)
        {
            throw new ArgumentException("Truncated length slice");
        }
        
        // Decode the lengths (big-endian uint16)
        var lengths = new ushort[numParts];
        for (int i = 0; i < numParts; i++)
        {
            lengths[i] = (ushort)((buf[offset] << 8) | buf[offset + 1]);
            offset += 2;
        }
        
        // Split each message
        var parts = new List<byte[]>();
        int truncated = 0;
        
        for (int idx = 0; idx < lengths.Length; idx++)
        {
            int msgLen = lengths[idx];
            
            if (buf.Length < offset + msgLen)
            {
                truncated = numParts - idx;
                break;
            }
            
            var slice = new byte[msgLen];
            Array.Copy(buf, offset, slice, 0, msgLen);
            offset += msgLen;
            parts.Add(slice);
        }
        
        return (truncated, parts);
    }
}
