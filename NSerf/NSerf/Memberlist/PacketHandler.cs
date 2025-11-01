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
        _logger?.LogInformation("[PACKET] Received {Size} bytes from {From}, first byte: {FirstByte}", buf.Length, from, buf[0]);
        _logger?.LogDebug("[PACKET] Received {Size} bytes from {From}", buf.Length, from);

        // Remove label header if present
        string packetLabel;
        try
        {
            (buf, packetLabel) = LabelHandler.RemoveLabelHeaderFromPacket(buf);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to remove label header from packet from {From}", from);
            return;
        }

        // Validate label
        if (_memberlist._config.SkipInboundLabelCheck)
        {
            if (!string.IsNullOrEmpty(packetLabel))
            {
                _logger?.LogError("Unexpected double packet label header from {From}", from);
                return;
            }
            // Set this from config so that the auth data assertions work below
            packetLabel = _memberlist._config.Label;
        }

        if (_memberlist._config.Label != packetLabel)
        {
            _logger?.LogError("Discarding packet with unacceptable label \"{Label}\" from {From}", packetLabel, from);
            return;
        }

        // Check encryption and decrypt if needed
        if (_memberlist._config.EncryptionEnabled())
        {
            var authData = System.Text.Encoding.UTF8.GetBytes(packetLabel);
            var keys = _memberlist._config.Keyring!.GetKeys();
            try
            {
                buf = Security.DecryptPayload([.. keys], buf, authData);
            }
            catch (Exception ex)
            {
                if (!_memberlist._config.GossipVerifyIncoming)
                {
                    // Treat the message as plaintext
                    _logger?.LogDebug("Failed to decrypt packet, treating as plaintext: {Error}", ex.Message);
                }
                else
                {
                    _logger?.LogError(ex, "Failed to decrypt packet from {From} - DROPPING PACKET!", from);
                    return;
                }
            }
        }

        // Check for CRC
        if (buf.Length >= 5 && (MessageType)buf[0] == MessageType.HasCrc)
        {
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
            // Decode the PingMessage using MessagePack like Go implementation
            var ping = Messages.MessageEncoder.Decode<Messages.PingMessage>(buf);

            // Verify node name if provided (Go does this)
            if (!string.IsNullOrEmpty(ping.Node) && ping.Node != _memberlist._config.Name)
            {
                _logger?.LogWarning("Got ping for unexpected node '{Node}' from {From}", ping.Node, from);
                return;
            }

            // Create ack response
            var ack = new Messages.AckRespMessage
            {
                SeqNo = ping.SeqNo,
                Payload = _memberlist._config.Ping?.AckPayload() ?? []
            };

            // Determine reply address - use source info if provided, otherwise use socket address
            string replyAddr;
            if (ping.SourceAddr != null && ping.SourceAddr.Length > 0 && ping.SourcePort > 0)
            {
                var sourceIp = new System.Net.IPAddress(ping.SourceAddr);
                replyAddr = $"{sourceIp}:{ping.SourcePort}";
            }
            else if (from is IPEndPoint ipep)
            {
                replyAddr = $"{ipep.Address}:{ipep.Port}";
            }
            else
            {
                _logger?.LogWarning("Cannot determine sender address for ping reply: {From}", from);
                return;
            }

            var addr = new Transport.Address
            {
                Addr = replyAddr,
                Name = ping.SourceNode ?? string.Empty
            };

            // Encode and send ack
            var ackBytes = Messages.MessageEncoder.Encode(MessageType.AckResp, ack);
            _ = _memberlist.SendPacketAsync(ackBytes, addr);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling ping from {From}", from);
        }
    }

    private void HandleIndirectPing(byte[] buf, EndPoint from)
    {
        try
        {
            // Decode indirect ping request
            var ind = Messages.MessageEncoder.Decode<Messages.IndirectPingMessage>(buf);

            // For proto versions < 2, there is no port provided
            // Use configured port as fallback
            if (_memberlist._config.ProtocolVersion < 2 || ind.Port == 0)
            {
                ind.Port = (ushort)_memberlist._config.BindPort;
            }

            // Send a ping to the target on behalf of the requester
            var localSeqNo = _memberlist.NextSequenceNum();
            var (selfAddr, selfPort) = _memberlist.GetAdvertiseAddr();

            var ping = new Messages.PingMessage
            {
                SeqNo = localSeqNo,
                Node = ind.Node,
                SourceAddr = selfAddr.GetAddressBytes(),
                SourcePort = (ushort)selfPort,
                SourceNode = _memberlist._config.Name
            };

            // Determine address to send ack back to
            string indAddr;
            string indName = ind.SourceNode;
            if (ind.SourceAddr != null && ind.SourceAddr.Length > 0 && ind.SourcePort > 0)
            {
                var sourceIp = new IPAddress(ind.SourceAddr);
                indAddr = $"{sourceIp}:{ind.SourcePort}";
            }
            else if (from is IPEndPoint ipep)
            {
                indAddr = $"{ipep.Address}:{ipep.Port}";
            }
            else
            {
                _logger?.LogWarning("Cannot determine source address for indirect ping from {From}", from);
                return;
            }

            // Setup response handler to forward the ack
            var ackHandler = new AckNackHandler(_logger);
            ackHandler.SetAckHandler(
                localSeqNo,
                (payload, timestamp) =>
                {
                    // Forward ack back to requester
                    var ack = new Messages.AckRespMessage
                    {
                        SeqNo = ind.SeqNo,
                        Payload = Array.Empty<byte>()
                    };
                    var ackAddr = new Transport.Address { Addr = indAddr, Name = indName };
                    _ = EncodeAndSendMessageAsync(ackAddr, Messages.MessageType.AckResp, ack);
                },
                () =>
                {
                    // Send nack if requested
                    if (ind.Nack)
                    {
                        var nackAddr = new Transport.Address { Addr = indAddr, Name = indName };
                        var nack = new Messages.NackRespMessage { SeqNo = ind.SeqNo };
                        _ = EncodeAndSendMessageAsync(nackAddr, Messages.MessageType.NackResp, nack);
                    }
                },
                TimeSpan.FromSeconds(5)
            );

            // Register handler temporarily
            _memberlist._ackHandlers[localSeqNo] = ackHandler;

            // Send ping to target
            var targetIp = new IPAddress(ind.Target);
            var targetAddr = new Transport.Address
            {
                Addr = $"{targetIp}:{ind.Port}",
                Name = ind.Node
            };
            _ = EncodeAndSendMessageAsync(targetAddr, Messages.MessageType.Ping, ping);

            _logger?.LogDebug("Forwarding indirect ping {SeqNo} to {Target} on behalf of {Source}",
                ind.SeqNo, ind.Node, from);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling indirect ping from {From}", from);
        }
    }

    private async Task EncodeAndSendMessageAsync<T>(Transport.Address addr, Messages.MessageType msgType, T message)
    {
        try
        {
            var encoded = Messages.MessageEncoder.Encode(msgType, message);
            await _memberlist.SendPacketAsync(encoded, addr);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send message type {Type} to {Addr}", msgType, addr);
        }
    }

    private void HandleAck(byte[] buf, EndPoint from, DateTimeOffset timestamp)
    {
        try
        {
            // Decode the AckRespMessage using MessagePack like Go implementation
            var ack = Messages.MessageEncoder.Decode<Messages.AckRespMessage>(buf);
            _logger?.LogDebug("Received ack seq={Seq} from {From}", ack.SeqNo, from);

            // Invoke ack handler if memberlist has an ack/nack handler
            if (_memberlist._ackHandlers.TryGetValue(ack.SeqNo, out var handler) && handler is AckNackHandler ackHandler)
            {
                ackHandler.InvokeAck(ack.SeqNo, ack.Payload ?? Array.Empty<byte>(), timestamp);
            }
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
                _logger?.LogError("Nack message too short from {From}", from);
                return;
            }
            uint seqNo = (uint)(buf[0] << 24 | buf[1] << 16 | buf[2] << 8 | buf[3]);
            _logger?.LogDebug("Received nack seq={Seq} from {From}", seqNo, from);

            // Invoke nack handler if memberlist has an ack/nack handler
            if (_memberlist._ackHandlers.TryGetValue(seqNo, out var handler) && handler is AckNackHandler ackHandler)
            {
                ackHandler.InvokeNack(seqNo);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling nack from {From}", from);
        }
    }

    private void QueueMessage(MessageType msgType, byte[] buf, EndPoint from)
    {
        _logger?.LogInformation("[QUEUE] Processing {MessageType} message from {From}", msgType, from);

        // Process messages synchronously for now
        var stateHandler = new StateHandlers(_memberlist, _logger);

        try
        {
            switch (msgType)
            {
                case MessageType.Alive:
                    var aliveMsg = Messages.MessageEncoder.Decode<Messages.AliveMessage>(buf);
                    var alive = new Messages.Alive
                    {
                        Incarnation = aliveMsg.Incarnation,
                        Node = aliveMsg.Node,
                        Addr = aliveMsg.Addr,
                        Port = aliveMsg.Port,
                        Meta = aliveMsg.Meta,
                        Vsn = aliveMsg.Vsn
                    };
                    stateHandler.HandleAliveNode(alive, false, null);
                    break;

                case MessageType.Suspect:
                    var suspectMsg = Messages.MessageEncoder.Decode<Messages.SuspectMessage>(buf);
                    var suspect = new Messages.Suspect
                    {
                        Incarnation = suspectMsg.Incarnation,
                        Node = suspectMsg.Node,
                        From = suspectMsg.From
                    };
                    stateHandler.HandleSuspectNode(suspect);
                    break;

                case MessageType.Dead:
                    _logger?.LogInformation("[QUEUE] Decoding Dead message, buf length: {Length}", buf.Length);
                    var deadMsg = Messages.MessageEncoder.Decode<Messages.DeadMessage>(buf);
                    _logger?.LogInformation("[QUEUE] Decoded Dead: Node={Node}, From={From}, Inc={Inc}", 
                        deadMsg.Node, deadMsg.From, deadMsg.Incarnation);
                    var dead = new Messages.Dead
                    {
                        Incarnation = deadMsg.Incarnation,
                        Node = deadMsg.Node,
                        From = deadMsg.From
                    };
                    stateHandler.HandleDeadNode(dead);
                    _logger?.LogInformation("[QUEUE] HandleDeadNode returned for {Node}", deadMsg.Node);
                    break;

                case MessageType.User:
                    // User messages are raw byte buffers passed directly to delegate
                    if (_memberlist._config.Delegate != null)
                    {
                        // Pass the raw buffer to the delegate
                        // Note: buf is the message payload after the message type byte
                        _memberlist._config.Delegate.NotifyMsg(buf);
                        _logger?.LogDebug("User message ({Size} bytes) delivered to delegate from {From}", buf.Length, from);
                    }
                    else
                    {
                        _logger?.LogDebug("User message ({Size} bytes) received from {From} but no delegate configured", buf.Length, from);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process {Type} message from {From}", msgType, from);
        }
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
