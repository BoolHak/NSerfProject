// Ported from: github.com/hashicorp/memberlist/label.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net.Sockets;
using NSerf.Memberlist.Messages;

namespace NSerf.Memberlist;

/// <summary>
/// Handles label prefixing for packets and streams.
/// Labels are used to prevent cross-talk between different memberlist clusters.
/// </summary>
public static class LabelHandler
{
    /// <summary>
    /// Maximum length of a packet or stream label.
    /// </summary>
    public const int LabelMaxSize = 255;
    
    /// <summary>
    /// Prefixes outgoing packets with the label header if the label is not empty.
    /// Format: [hasLabelMsg:byte][length:byte][label bytes]
    /// </summary>
    public static byte[] AddLabelHeaderToPacket(byte[] buf, string label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return buf;
        }
        
        if (label.Length > LabelMaxSize)
        {
            throw new ArgumentException($"Label \"{label}\" is too long", nameof(label));
        }
        
        return MakeLabelHeader(label, buf);
    }
    
    /// <summary>
    /// Removes any label header from the provided packet and returns it along with remaining contents.
    /// </summary>
    public static (byte[] Buffer, string Label) RemoveLabelHeaderFromPacket(byte[] buf)
    {
        if (buf.Length == 0)
        {
            return (buf, string.Empty);
        }
        
        var msgType = (MessageType)buf[0];
        if (msgType != MessageType.HasLabel)
        {
            return (buf, string.Empty);
        }
        
        if (buf.Length < 2)
        {
            throw new InvalidOperationException("Cannot decode label; packet has been truncated");
        }
        
        int size = buf[1];
        if (size < 1)
        {
            throw new InvalidOperationException("Label header cannot be empty when present");
        }
        
        if (buf.Length < 2 + size)
        {
            throw new InvalidOperationException("Cannot decode label; packet has been truncated");
        }
        
        string label = System.Text.Encoding.UTF8.GetString(buf, 2, size);
        byte[] newBuf = new byte[buf.Length - 2 - size];
        Array.Copy(buf, 2 + size, newBuf, 0, newBuf.Length);
        
        return (newBuf, label);
    }
    
    /// <summary>
    /// Prefixes outgoing streams with the label header if the label is not empty.
    /// </summary>
    public static async Task AddLabelHeaderToStreamAsync(NetworkStream stream, string label, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(label))
        {
            return;
        }
        
        if (label.Length > LabelMaxSize)
        {
            throw new ArgumentException($"Label \"{label}\" is too long", nameof(label));
        }
        
        byte[] header = MakeLabelHeader(label, null);
        await stream.WriteAsync(header, cancellationToken);
    }
    
    /// <summary>
    /// Removes any label header from the beginning of the stream if present.
    /// Returns the label and a buffer containing any peeked data that needs to be read first.
    /// </summary>
    public static async Task<(string Label, byte[]? PeekedData)> RemoveLabelHeaderFromStreamAsync(
        NetworkStream stream, 
        CancellationToken cancellationToken = default)
    {
        // Peek first byte to check for label
        byte[] typeBuf = new byte[1];
        int read = await stream.ReadAsync(typeBuf, cancellationToken);
        
        if (read == 0)
        {
            return (string.Empty, null);
        }
        
        var msgType = (MessageType)typeBuf[0];
        if (msgType != MessageType.HasLabel)
        {
            // Return the peeked byte to be processed
            return (string.Empty, typeBuf);
        }
        
        // Read size byte
        byte[] sizeBuf = new byte[1];
        read = await stream.ReadAsync(sizeBuf, cancellationToken);
        
        if (read == 0)
        {
            throw new InvalidOperationException("Cannot decode label; stream has been truncated");
        }
        
        int size = sizeBuf[0];
        if (size < 1)
        {
            throw new InvalidOperationException("Label header cannot be empty when present");
        }
        
        // Read label bytes
        byte[] labelBuf = new byte[size];
        int totalRead = 0;
        while (totalRead < size)
        {
            read = await stream.ReadAsync(labelBuf.AsMemory(totalRead, size - totalRead), cancellationToken);
            if (read == 0)
            {
                throw new InvalidOperationException("Cannot decode label; stream has been truncated");
            }
            totalRead += read;
        }
        
        string label = System.Text.Encoding.UTF8.GetString(labelBuf);
        return (label, null);
    }
    
    /// <summary>
    /// Calculates the overhead (in bytes) added by a label header.
    /// </summary>
    public static int LabelOverhead(string label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return 0;
        }
        return 2 + System.Text.Encoding.UTF8.GetByteCount(label);
    }
    
    private static byte[] MakeLabelHeader(string label, byte[]? rest)
    {
        byte[] labelBytes = System.Text.Encoding.UTF8.GetBytes(label);
        int totalLength = 2 + labelBytes.Length + (rest?.Length ?? 0);
        
        byte[] newBuf = new byte[totalLength];
        newBuf[0] = (byte)MessageType.HasLabel;
        newBuf[1] = (byte)labelBytes.Length;
        
        Array.Copy(labelBytes, 0, newBuf, 2, labelBytes.Length);
        
        if (rest != null && rest.Length > 0)
        {
            Array.Copy(rest, 0, newBuf, 2 + labelBytes.Length, rest.Length);
        }
        
        return newBuf;
    }
}
