// Ported from: github.com/hashicorp/memberlist/net.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Net;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Common;

namespace NSerf.Memberlist;

/// <summary>
/// Handles incoming packet processing for Memberlist.
/// </summary>
internal class PacketHandler
{
    private readonly Memberlist _memberlist;
    private readonly ILogger? _logger;
    
    public PacketHandler(Memberlist memberlist, ILogger? logger)
    {
        _memberlist = memberlist;
        _logger = logger;
    }
    
    /// <summary>
    /// Ingests and processes an incoming packet.
    /// </summary>
    public void IngestPacket(byte[] buf, EndPoint from, DateTimeOffset timestamp)
    {
        // TODO: Remove label header
        var packetLabel = string.Empty;
        
        // TODO: Check encryption and decrypt if needed
        
        // Check for CRC
        if (buf.Length >= 5 && (MessageType)buf[0] == MessageType.HasCrc)
        {
            // TODO: Verify CRC
            var crc = Crc32.Compute(buf, 5, buf.Length - 5);
            var expected = BitConverter.ToUInt32(buf, 1);
            
            if (BitConverter.IsLittleEndian)
            {
                expected = ReverseBytes(expected);
            }
            
            if (crc != expected)
            {
                _logger?.LogWarning("Got invalid checksum for UDP packet: {Crc:X}, {Expected:X}", crc, expected);
                return;
            }
            
            HandleCommand(buf[5..], from, timestamp);
        }
        else
        {
            HandleCommand(buf, from, timestamp);
        }
    }
    
    /// <summary>
    /// Handles a command message.
    /// </summary>
    public void HandleCommand(byte[] buf, EndPoint from, DateTimeOffset timestamp)
    {
        if (buf.Length < 1)
        {
            _logger?.LogError("Missing message type byte from {From}", from);
            return;
        }
        
        var msgType = (MessageType)buf[0];
        var msgBuf = buf[1..];
        
        switch (msgType)
        {
            case MessageType.Compound:
                HandleCompound(msgBuf, from, timestamp);
                break;
                
            case MessageType.Compress:
                HandleCompressed(msgBuf, from, timestamp);
                break;
                
            case MessageType.Ping:
                HandlePing(msgBuf, from);
                break;
                
            case MessageType.IndirectPing:
                HandleIndirectPing(msgBuf, from);
                break;
                
            case MessageType.AckResp:
                HandleAck(msgBuf, from, timestamp);
                break;
                
            case MessageType.NackResp:
                HandleNack(msgBuf, from);
                break;
                
            case MessageType.Suspect:
            case MessageType.Alive:
            case MessageType.Dead:
            case MessageType.User:
                // Queue for async processing
                QueueMessage(msgType, msgBuf, from);
                break;
                
            default:
                _logger?.LogError("Message type ({Type}) not supported from {From}", (int)msgType, from);
                break;
        }
    }
    
    private void HandleCompound(byte[] buf, EndPoint from, DateTimeOffset timestamp)
    {
        // Decode and re-dispatch each inner message
        var (truncated, parts) = CompoundMessage.DecodeCompoundMessage(buf);

        if (truncated > 0)
        {
            _logger?.LogWarning("Compound request had {Truncated} truncated messages from {From}", truncated, from);
        }

        foreach (var part in parts)
        {
            HandleCommand(part, from, timestamp);
        }
    }
    
    private void HandleCompressed(byte[] buf, EndPoint from, DateTimeOffset timestamp)
    {
        // Decompress and re-dispatch the inner payload
        try
        {
            var payload = CompressionUtils.DecompressPayload(buf);
            HandleCommand(payload, from, timestamp);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to decompress payload from {From}", from);
        }
    }
    
    private void HandlePing(byte[] buf, EndPoint from)
    {
        try
        {
            if (buf.Length < 4)
            {
                _logger?.LogWarning("Ping payload too short from {From}", from);
                return;
            }

            // Decode SeqNo (big-endian)
            uint seqNo = (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);

            // Build ack response: [AckResp type][seqno]
            var outBuf = new byte[1 + 4];
            outBuf[0] = (byte)MessageType.AckResp;
            outBuf[1] = (byte)(seqNo >> 24);
            outBuf[2] = (byte)(seqNo >> 16);
            outBuf[3] = (byte)(seqNo >> 8);
            outBuf[4] = (byte)(seqNo);

            // Send back to sender
            if (from is IPEndPoint ipep)
            {
                var addr = new Transport.Address { Addr = $"{ipep.Address}:{ipep.Port}", Name = string.Empty };
                _ = _memberlist.SendPacketAsync(outBuf, addr);
            }
            else
            {
                _logger?.LogWarning("Cannot determine sender address for ping reply: {From}", from);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling ping from {From}", from);
        }
    }
    
    private void HandleIndirectPing(byte[] buf, EndPoint from)
    {
        // TODO: Decode indirect ping and forward
        _logger?.LogDebug("Handling indirect ping from {From}", from);
    }
    
    private void HandleAck(byte[] buf, EndPoint from, DateTimeOffset timestamp)
    {
        try
        {
            if (buf.Length < 4)
            {
                _logger?.LogWarning("Ack payload too short from {From}", from);
                return;
            }
            uint seqNo = (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);
            _logger?.LogDebug("Received ack seq={Seq} from {From}", seqNo, from);
            // TODO: Integrate with AckNackHandler to resolve awaiting probes
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling ack from {From}", from);
        }
    }
    
    private void HandleNack(byte[] buf, EndPoint from)
    {
        try
        {
            if (buf.Length < 4)
            {
                _logger?.LogWarning("Nack payload too short from {From}", from);
                return;
            }
            uint seqNo = (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);
            _logger?.LogDebug("Received nack seq={Seq} from {From}", seqNo, from);
            // TODO: Integrate with AckNackHandler to resolve awaiting probes
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling nack from {From}", from);
        }
    }
    
    private void QueueMessage(MessageType msgType, byte[] buf, EndPoint from)
    {
        // TODO: Queue message for async processing
        _logger?.LogDebug("Queuing message type {Type} from {From}", msgType, from);
    }
    
    private static uint ReverseBytes(uint value)
    {
        return (value & 0x000000FFU) << 24 |
               (value & 0x0000FF00U) << 8 |
               (value & 0x00FF0000U) >> 8 |
               (value & 0xFF000000U) >> 24;
    }
}

/// <summary>
/// Simple CRC32 implementation.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();
    
    private static uint[] BuildTable()
    {
        const uint poly = 0xedb88320;
        var table = new uint[256];
        
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ poly;
                else
                    crc >>= 1;
            }
            table[i] = crc;
        }
        
        return table;
    }
    
    public static uint Compute(byte[] buffer, int offset, int count)
    {
        uint crc = 0xFFFFFFFF;
        
        for (int i = offset; i < offset + count; i++)
        {
            byte index = (byte)((crc & 0xFF) ^ buffer[i]);
            crc = (crc >> 8) ^ Table[index];
        }
        
        return ~crc;
    }
}
