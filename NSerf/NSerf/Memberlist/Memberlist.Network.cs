using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Exceptions;
using NSerf.Memberlist.Handlers;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Security;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

public partial class Memberlist
{
    /// <summary>
    /// Performs periodic push-pull with a random node for full state synchronization.
    /// This ensures eventual consistency across the cluster.
    /// </summary>
    private async Task PeriodicPushPullAsync()
    {
        if (IsLeaving) return;
        var randomNode = PickARandomNode();
        if(randomNode is not null) await PushPullWithNode(randomNode);
    }

    private async Task PushPullWithNode(NodeState nodeState)
    {
        var addr = new Address
        {
            Addr = $"{nodeState.Node.Addr}:{nodeState.Node.Port}",
            Name = nodeState.Name
        };

        try
        {
            _logger?.LogDebug("[PUSH-PULL] Starting periodic push-pull with {Node}", nodeState.Name);
            await PushPullNodeAsync(addr, join: false, CancellationToken.None);
            _logger?.LogDebug("[PUSH-PULL] Completed periodic push-pull with {Node}", nodeState.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[PUSH-PULL] Failed periodic push-pull with {Node}", nodeState.Name);
        }
    }

    private NodeState? PickARandomNode()
    {
        NodeState? nodeState;
        lock (NodeLock)
        {
            if (Nodes.Count == 0) return null;
            var index = Random.Shared.Next(Nodes.Count);
            nodeState = Nodes[index];
        }

        return nodeState;
    }

    /// <summary>
    /// Performs one round of failure detection by probing a node.
    /// This is called periodically by the probe scheduler.
    /// </summary>
    private async Task ProbeAsync()
    {
        if (IsLeaving) return;

        var numCheck = 0;
        int maxChecks;

        lock (NodeLock)
        {
            maxChecks = Nodes.Count;
            if (maxChecks == 0) return; 
        }

        while (numCheck < maxChecks)
        {
            NodeState? nodeToProbe;

            lock (NodeLock)
            {
                if (Nodes.Count == 0) return; 
                if (_probeIndex >= Nodes.Count) _probeIndex = 0;

                var node = Nodes[_probeIndex];
                _probeIndex++;
                numCheck++;

                // Skip local node
                if (node.Name == Config.Name || node.State is NodeStateType.Dead or NodeStateType.Left)
                {
                    continue;
                }
                nodeToProbe = node;
            }

            await ProbeNodeAsync(nodeToProbe);
            return;
        }

        // No suitable nodes found after checking all
        _logger?.LogDebug("No suitable nodes to probe after checking {Count} nodes", numCheck);
    }

    /// <summary>
    /// Probes a specific node to check if it's alive using UDP ping.
    /// </summary>
    private async Task ProbeNodeAsync(NodeState node)
    {
        _logger?.LogDebug("Probing node: {Node}", node.Name);

        // Get a sequence number for this probe
        var seqNo = NextSequenceNum();

        // Setup ack handler to wait for response
        var ackReceived = new TaskCompletionSource<bool>();
        var handler = new AckNackHandler(_logger);

        handler.SetAckHandler(
            seqNo,
            (_, _) =>
            {
                ackReceived.TrySetResult(true); // Success
            },
            () =>
            {
                ackReceived.TrySetResult(false); // Timeout/Nack
            },
            Config.ProbeTimeout
        );

        AckHandlers[seqNo] = handler;

        try
        {
            // Encode ping using MessagePack like Go implementation
            var (advertiseAddr, advertisePort) = GetAdvertiseAddr();
            var pingMsg = new PingMessage
            {
                SeqNo = seqNo,
                Node = node.Name, // Target node name for verification
                SourceAddr = advertiseAddr.GetAddressBytes(),
                SourcePort = (ushort)advertisePort,
                SourceNode = Config.Name
            };

            var pingBytes = MessageEncoder.Encode(MessageType.Ping, pingMsg);
            var addr = new Address
            {
                Addr = $"{node.Node.Addr}:{node.Node.Port}",
                Name = node.Name
            };

            await SendPacketAsync(pingBytes, addr, _shutdownCts.Token);

            // Start TCP fallback in parallel if enabled and node supports it
            // Go implementation does this for a protocol version >= 3
            var tcpFallbackTask = Task.FromResult(false);
            var disableTcpPings = Config.DisableTcpPings ||
                                  (Config.DisableTcpPingsForNode != null && Config.DisableTcpPingsForNode(node.Name));

            if (!disableTcpPings && node.Node.PMax >= 3)
            {
                tcpFallbackTask = Task.Run(async () =>
                {
                    try
                    {
                        var deadline = DateTimeOffset.UtcNow.Add(Config.ProbeTimeout);
                        var didContact = await SendPingAndWaitForAckAsync(addr, pingMsg, deadline, _shutdownCts.Token);
                        return didContact;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("Failed fallback TCP ping to {Node}: {Error}", node.Name, ex.Message);
                        return false;
                    }
                });
            }

            // Wait for UDP ack or timeout
            var success = await ackReceived.Task;

            if (success)
            {
                _logger?.LogDebug("Probe successful for node: {Node}", node.Name);

                // Cancel any suspicion timer since the node responded successfully
                if (!NodeTimers.TryRemove(node.Name, out var timerObj) || timerObj is not Suspicion suspicion) return;

                try
                {
                    suspicion.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Timer already disposed, safe to ignore
                }

                return;
            }

            // UDP failed, check a TCP fallback result
            var tcpSuccess = await tcpFallbackTask;
            if (tcpSuccess)
            {
                _logger?.LogWarning(
                    "Was able to connect to {Node} over TCP but UDP probes failed, network may be misconfigured",
                    node.Name);

                // Cancel any suspicion timer
                if (!NodeTimers.TryRemove(node.Name, out var timerObj) || timerObj is not Suspicion suspicion) return;
                try
                {
                    suspicion.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Timer already disposed, safe to ignore
                }

                return;
            }

            // Both UDP and TCP failed - mark as suspect
            // Don't mark nodes as suspect if we're leaving
            if (IsLeaving) return;

            _logger?.LogWarning("Probe failed for node: {Node}, marking as suspect", node.Name);

            // Mark the node as suspect
            var suspect = new Suspect
            {
                Incarnation = node.Incarnation,
                Node = node.Name,
                From = Config.Name
            };

            var stateHandler = new StateHandlers(this, _logger);
            stateHandler.HandleSuspectNode(suspect);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error probing node: {Node}", node.Name);
        }
        finally
        {
            AckHandlers.TryRemove(seqNo, out _);
        }
    }

    /// <summary>
    /// Gossip is invoked every GossipInterval to broadcast our gossip messages
    /// to a few random nodes.
    /// Made internal to allow LeaveManager to force immediate gossip during graceful leave.
    /// </summary>
    internal async Task GossipAsync(CancellationToken cancellationToken = default)
    {
        // Get some random live, suspect, or recently dead nodes
        List<Node> kNodes;
        lock (NodeLock)
        {
            kNodes = NodeStateManager.KRandomNodes(Config.GossipNodes, Nodes, node =>
            {
                // Exclude self
                if (node.Name == Config.Name)
                    return true;

                // Include live and suspect nodes
                return node.State switch
                {
                    NodeStateType.Alive or NodeStateType.Suspect => false,
                    NodeStateType.Dead or NodeStateType.Left => DateTimeOffset.UtcNow - node.StateChange > Config.GossipToTheDeadTime,
                    _ => true, // Exclude other states
                };
            });
        }

        if (kNodes.Count == 0) return;

        // Calculate bytes available for broadcasts
        var bytesAvail = Config.UDPBufferSize - MessageConstants.CompoundHeaderOverhead;

        _logger?.LogDebug("[GOSSIP] Starting gossip round to {Count} nodes, {Queued} broadcasts queued", kNodes.Count,
            Broadcasts.NumQueued());

        // CRITICAL: Get broadcasts ONCE per gossip interval, not per node!
        // The same messages are sent to all K random nodes in this interval
        var messages = GetBroadcasts(MessageConstants.CompoundOverhead, bytesAvail);

        if (messages.Count == 0)
        {
            _logger?.LogDebug("[GOSSIP] No broadcasts available, skipping gossip round");
            return;
        }

        foreach (var node in kNodes)
        {
            _logger?.LogInformation("[GOSSIP] *** Sending {Count} broadcasts to {Node} ***", messages.Count, node.Name);

            var addr = new Address
            {
                Addr = $"{node.Addr}:{node.Port}",
                Name = node.Name
            };

            try
            {
                // Send a single message as-is
                var packet = messages.Count == 1 ? messages[0] : CompoundMessage.MakeCompoundMessage(messages);

                // Encrypt if enabled (BEFORE adding label header, matching SendPacketAsync)
                if (Config.EncryptionEnabled() && Config.GossipVerifyOutgoing)
                {
                    try
                    {
                        var authData = System.Text.Encoding.UTF8.GetBytes(Config.Label ?? "");
                        var primaryKey = Config.Keyring?.GetPrimaryKey() ??
                                         throw new InvalidOperationException("No primary key available");
                        packet = SecurityTools.EncryptPayload(1, primaryKey, packet, authData);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[GOSSIP] Failed to encrypt packet for {Node}", node.Name);
                        throw;
                    }
                }

                // Add a label header if configured (AFTER encryption, matching SendPacketAsync)
                if (!string.IsNullOrEmpty(Config.Label))
                {
                    packet = LabelHandler.AddLabelHeaderToPacket(packet, Config.Label);
                }

                // Use the provided token (not shutdown token) to allow leave messages during shutdown
                var tokenToUse = cancellationToken == CancellationToken.None ? _shutdownCts.Token : cancellationToken;
                await _transport.WriteToAddressAsync(packet, addr, tokenToUse);

                _logger?.LogDebug("[GOSSIP] Successfully sent {Count} messages to {Node} at {Addr}", messages.Count,
                    node.Name, addr.Addr);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to send gossip to {Node}", node.Name);
            }
        }
    }

    /// <summary>
    /// Handles an incoming TCP stream connection.
    /// </summary>
    private async Task HandleStreamAsync(NetworkStream stream)
    {
        try
        {
            _logger?.LogDebug("Accepted incoming TCP connection");
            stream.Socket.ReceiveTimeout = (int)Config.TCPTimeout.TotalMilliseconds;
            stream.Socket.SendTimeout = (int)Config.TCPTimeout.TotalMilliseconds;

            // Remove and validate label header from stream
            var (streamLabel, peekedData) =
                await LabelHandler.RemoveLabelHeaderFromStreamAsync(stream, _shutdownCts.Token);

            // Validate label
            if (Config.SkipInboundLabelCheck)
            {
                if (!string.IsNullOrEmpty(streamLabel))
                {
                    _logger?.LogError("Unexpected double stream label header");
                    return;
                }

                // Set this from config so that the auth data assertions work below
                streamLabel = Config.Label;
            }

            if (Config.Label != streamLabel)
            {
                _logger?.LogError("Discarding stream with unacceptable label \"{Label}\"", streamLabel);
                return;
            }

            // Read message type (either from peeked data or from stream)
            byte[] buffer;
            if (peekedData is { Length: > 0 })
            {
                // Use peeked data (label wasn't present)
                buffer = peekedData;
            }
            else
            {
                // Read message type from stream (label was present and removed)
                buffer = new byte[1];
                var bytesRead = await stream.ReadAsync(buffer, _shutdownCts.Token);
                if (bytesRead == 0)
                {
                    _logger?.LogDebug("Connection closed before message type received");
                    return;
                }
            }

            // Read 4-byte length prefix for TCP framing (always present)
            var lengthBytes = new byte[4];
            lengthBytes[0] = buffer[0]; // First byte already read
            var totalRead = 1;
            while (totalRead < 4)
            {
                var bytesRead =
                    await stream.ReadAsync(lengthBytes.AsMemory(totalRead, 4 - totalRead), _shutdownCts.Token);
                if (bytesRead == 0)
                    throw new IOException("Connection closed while reading message length prefix");

                totalRead += bytesRead;
            }

            if (BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);

            var messageLength = BitConverter.ToInt32(lengthBytes, 0);
            _logger?.LogDebug("Reading {Length} bytes of message data", messageLength);

            // Read exact message payload based on length prefix
            var messageData = new byte[messageLength];
            totalRead = 0;
            while (totalRead < messageLength)
            {
                var bytesRead = await stream.ReadAsync(messageData.AsMemory(totalRead, messageLength - totalRead),
                    _shutdownCts.Token);
                if (bytesRead == 0)
                    throw new IOException(
                        $"Connection closed while reading message payload ({totalRead}/{messageLength} bytes read)");

                totalRead += bytesRead;
            }

            // Check if encryption is enabled - decrypt if needed
            if (Config.EncryptionEnabled())
            {
                _logger?.LogDebug("Decrypting incoming message (encryption enabled in config)");

                try
                {
                    var authData = System.Text.Encoding.UTF8.GetBytes(streamLabel);
                    messageData = SecurityTools.DecryptPayload([.. Config.Keyring!.GetKeys()], messageData, authData);
                }
                catch (Exception ex)
                {
                    if (Config.GossipVerifyIncoming)
                    {
                        _logger?.LogError(ex, "Failed to decrypt stream");
                        return;
                    }

                    _logger?.LogDebug(ex, "Failed to decrypt stream, treating as plaintext");
                }
            }

            // Now process the message (decrypted if needed)
            if (messageData.Length > 0)
            {
                var msgType = (MessageType)messageData[0];
                _logger?.LogDebug("Message type: {MessageType}", msgType);

                // Handle compression wrapper
                if (msgType == MessageType.Compress)
                {
                    _logger?.LogDebug("Decompressing message");

                    try
                    {
                        // Decompress the payload (skip message type byte)
                        var compressedData = messageData[1..];
                        var decompressedPayload = Common.CompressionUtils.DecompressPayload(compressedData);

                        if (decompressedPayload.Length > 0)
                        {
                            msgType = (MessageType)decompressedPayload[0];
                            _logger?.LogDebug("Decompressed message type: {MessageType}", msgType);

                            // Create a memory stream from a decompressed payload (skip message type byte)
                            using var decompressedStream =
                                new MemoryStream(decompressedPayload, 1, decompressedPayload.Length - 1);
                            await ProcessInnerMessageAsync(msgType, decompressedStream, stream);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to decompress message");
                    }
                }
                else
                {
                    // Direct message - create stream from messageData
                    // For PushPull, don't skip message type because ReadRemoteStateAsync expects the full structure
                    using var messageStream = new MemoryStream(messageData, 1, messageData.Length - 1);
                    await ProcessInnerMessageAsync(msgType, messageStream, stream);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling stream");
            throw;
        }
    }

    /// <summary>
    /// Processes the actual message type after decryption/decompression.
    /// </summary>
    private async Task ProcessInnerMessageAsync(MessageType msgType, Stream payloadStream, NetworkStream originalStream)
    {
        try
        {
            switch (msgType)
            {
                case MessageType.PushPull:
                    await HandlePushPullStreamAsync(payloadStream, originalStream);
                    break;
                case MessageType.User:
                    await HandleUserMsgStreamAsync(payloadStream, originalStream);
                    break;
                default:
                    _logger?.LogWarning("Unknown stream message type: {MessageType}", msgType);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling stream");
            throw;
        }
    }

    /// <summary>
    /// Handles an incoming user message over TCP stream.
    /// </summary>
    private async Task HandleUserMsgStreamAsync(Stream payloadStream, NetworkStream responseStream)
    {
        try
        {
            // Read the user message header using MessagePack deserialization
            var header =
                await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.UserMsgHeader>(payloadStream,
                    cancellationToken: _shutdownCts.Token);

            _logger?.LogDebug("User message header: {Length} bytes", header.UserMsgLen);

            // Read the user message payload (raw bytes, not MessagePack)
            if (header.UserMsgLen > 0)
            {
                var userMsgBytes = new byte[header.UserMsgLen];
                var totalRead = 0;

                while (totalRead < header.UserMsgLen)
                {
                    var bytesRead = await payloadStream.ReadAsync(
                        userMsgBytes.AsMemory(totalRead, header.UserMsgLen - totalRead), _shutdownCts.Token);
                    if (bytesRead == 0)
                        throw new IOException("Connection closed while reading user message");

                    totalRead += bytesRead;
                }

                // Pass to delegate
                if (Config.Delegate != null)
                {
                    Config.Delegate.NotifyMsg(userMsgBytes);
                    _logger?.LogDebug("User message ({Size} bytes) delivered to delegate via TCP", userMsgBytes.Length);
                }
                else
                {
                    _logger?.LogDebug("User message ({Size} bytes) received via TCP but no delegate configured",
                        userMsgBytes.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling user message stream");
            throw;
        }
    }

    /// <summary>
    /// Handles an incoming push/pull request over TCP.
    /// </summary>
    private async Task HandlePushPullStreamAsync(Stream payloadStream, NetworkStream responseStream)
    {
        try
        {
            // Read remote state from the payload stream 
            var (remoteNodes, userState) = await ReadRemoteStateAsync(payloadStream, _shutdownCts.Token);
            _logger?.LogDebug("Received {Count} nodes from remote", remoteNodes.Count);

            // Merge remote state
            var stateHandler = new StateHandlers(this, _logger);
            stateHandler.MergeRemoteState(remoteNodes);

            // Handle user state through delegate if needed
            if (userState is { Length: > 0 } && Config.Delegate != null)
            {
                try
                {
                    Config.Delegate.MergeRemoteState(userState, join: false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to merge remote user state");
                }
            }

            // Send our state back on the response stream
            await SendLocalStateAsync(responseStream, join: false, Config.Label, _shutdownCts.Token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Push/pull stream handler error");

            // Try to send error response on response stream
            try
            {
                await responseStream.WriteAsync(new byte[] { (byte)MessageType.Err });
                await responseStream.FlushAsync();
            }
            catch (Exception errEx)
            {
                // Errors when sending error response are expected (connection may be closed)
                _logger?.LogDebug(errEx, "Failed to send error response to stream");
            }
        }
    }

    /// <summary>
    /// Sends a raw UDP packet to a given Address via the transport, adding label header if configured.
    /// Also encrypts the packet if encryption is enabled and GossipVerifyOutgoing is true.
    /// </summary>
    internal async Task SendPacketAsync(byte[] buffer, Transport.Address addr,
        CancellationToken cancellationToken = default)
    {
        // Get label for both header and auth data
        var label = Config.Label ?? "";

        // Encrypt a UDP packet if enabled (before adding label header)
        // This matches the TCP encryption behavior in RawSendMsgStreamAsync
        if (Config.EncryptionEnabled() && Config.GossipVerifyOutgoing)
        {
            try
            {
                if (Config.Keyring == null)
                {
                    throw new InvalidOperationException("Encryption is enabled but no keyring is configured");
                }

                var authData = System.Text.Encoding.UTF8.GetBytes(label);
                var primaryKey = Config.Keyring.GetPrimaryKey() ??
                                 throw new InvalidOperationException("No primary key available");
                buffer = SecurityTools.EncryptPayload(1, primaryKey, buffer, authData);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to encrypt UDP packet");
                throw;
            }
        }

        // Add a label header if configured (after encryption, so the label is not encrypted)
        if (!string.IsNullOrEmpty(label))
        {
            buffer = LabelHandler.AddLabelHeaderToPacket(buffer, label);
        }

        await _transport.WriteToAddressAsync(buffer, addr, cancellationToken);
    }

    /// <summary>
    /// Sends a UDP packet to an address, adding a label header if configured.
    /// Public API matching Go's SendToAddress for query responses.
    /// </summary>
    public async Task SendToAddress(Address addr, byte[] buffer, CancellationToken cancellationToken = default)
    {
        await SendPacketAsync(buffer, addr, cancellationToken);
    }

    /// <summary>
    /// Sends a raw message over a stream with proper TCP framing.
    /// Format: [4-byte length prefix][message data (possibly compressed/encrypted)]
    /// </summary>
    private async Task RawSendMsgStreamAsync(NetworkStream conn, byte[] buf, string streamLabel,
        CancellationToken cancellationToken = default)
    {
        // Note: Label header should be added once when the connection is created (in transport layer),
        // not for every message sent on the stream.

        // Apply compression if enabled
        if (Config.EnableCompression)
        {
            try
            {
                var compressed = Common.CompressionUtils.CompressPayload(buf);
                // Prepend Compress message type byte (don't use MessageEncoder.Encode as it would MessagePack-serialize the byte array)
                var compressedWithType = new byte[1 + compressed.Length];
                compressedWithType[0] = (byte)Messages.MessageType.Compress;
                Buffer.BlockCopy(compressed, 0, compressedWithType, 1, compressed.Length);
                buf = compressedWithType;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to compress payload");
            }
        }

        // Apply encryption if enabled
        if (Config.EncryptionEnabled() && Config.GossipVerifyOutgoing)
        {
            try
            {
                if (Config.Keyring == null)
                {
                    throw new InvalidOperationException("Encryption is enabled but no keyring is configured");
                }

                var authData = System.Text.Encoding.UTF8.GetBytes(streamLabel);
                var primaryKey = Config.Keyring.GetPrimaryKey() ??
                                 throw new InvalidOperationException("No primary key available");
                buf = SecurityTools.EncryptPayload(1, primaryKey, buf, authData);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to encrypt payload");
                throw;
            }
        }

        // Always add a length prefix for proper TCP framing
        using var framedMs = new MemoryStream();
        var lengthBytes = BitConverter.GetBytes(buf.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(lengthBytes);
        }

        await framedMs.WriteAsync(lengthBytes, cancellationToken);
        await framedMs.WriteAsync(buf, cancellationToken);

        var framedMessage = framedMs.ToArray();
        await conn.WriteAsync(framedMessage, cancellationToken);
        await conn.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a ping and waits for an ack over a TCP stream.
    /// </summary>
    private async Task<bool> SendPingAndWaitForAckAsync(Address addr, Messages.PingMessage ping,
        DateTimeOffset deadline, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(addr.Name) && Config.RequireNodeNames)
        {
            throw new InvalidOperationException("Node names are required");
        }

        NetworkStream? conn = null;
        try
        {
            var timeout = deadline - DateTimeOffset.UtcNow;
            if (timeout <= TimeSpan.Zero)
            {
                return false;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            // Dial the target
            conn = await _transport.DialAddressTimeoutAsync(addr, timeout, cts.Token);

            // Add label header to stream if configured (must be first thing sent)
            if (!string.IsNullOrEmpty(Config.Label))
            {
                await LabelHandler.AddLabelHeaderToStreamAsync(conn, Config.Label, cts.Token);
            }

            // Encode the ping message
            var pingBytes = Messages.MessageEncoder.Encode(Messages.MessageType.Ping, ping);

            // Send the ping
            await RawSendMsgStreamAsync(conn, pingBytes, Config.Label, cts.Token);

            // Read the response
            var response = new byte[1024];
            var bytesRead = await conn.ReadAsync(response, cts.Token);

            if (bytesRead == 0)
            {
                return false;
            }

            // Check message type
            if (response[0] != (byte)Messages.MessageType.AckResp)
            {
                _logger?.LogWarning("Unexpected message type {Type} from ping", response[0]);
                return false;
            }

            // Decode ack
            var ack = Messages.MessageEncoder.Decode<Messages.AckRespMessage>(response.AsSpan(1, bytesRead - 1));

            if (ack.SeqNo == ping.SeqNo) return true;
            _logger?.LogWarning("Sequence number mismatch: expected {Expected}, got {Actual}", ping.SeqNo, ack.SeqNo);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error during ping to {Address}", addr);
            return false;
        }
        finally
        {
            if (conn is not null) await conn.DisposeAsync();
        }
    }

    /// <summary>
    /// Leave will broadcast a leave message but will not shut down the background
    /// listeners, meaning the node will continue participating in gossip and state
    /// updates.
    /// This method is safe to call multiple times.
    /// </summary>
    public async Task<Exception?> LeaveAsync(TimeSpan timeout)
    {
        // Set leave a flag to prevent gossiping Alive messages for ourselves
        if (Interlocked.CompareExchange(ref _leave, 1, 0) != 0)
            // Already left
            return null;

        // Cancel all suspicion timers to prevent marking healthy nodes as dead
        foreach (var kvp in NodeTimers)
        {
            if (kvp.Value is not Suspicion suspicion) continue;
            try
            {
                suspicion.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Timer already disposed, safe to ignore
            }
        }

        NodeTimers.Clear();

        // Update our own state to Left
        lock (NodeLock)
        {
            if (NodeMap.TryGetValue(Config.Name, out var ourState))
            {
                ourState.State = NodeStateType.Left;
                ourState.StateChange = DateTimeOffset.UtcNow;
            }
        }

        var leaveManager = new LeaveManager(this, _logger);
        var result = await leaveManager.LeaveAsync(Config.Name, timeout);

        return result.Success ? null : result.Error;
    }

    /// <summary>
    /// Join is used to take an existing Memberlist and attempt to join a cluster
    /// by contacting all the given hosts and performing a state sync.
    /// Returns the number of nodes successfully contacted and an error if none could be reached.
    /// </summary>
    public async Task<(int NumJoined, Exception? Error)> JoinAsync(IEnumerable<string> existing,
        CancellationToken cancellationToken = default)
    {
        var numSuccess = 0;
        var errors = new List<Exception>();

        foreach (var exist in existing)
        {
            try
            {
                // Parse address format: "NodeName/IP:Port" or "IP:Port"
                var nodeName = "";
                var addr = exist;

                if (exist.Contains('/'))
                {
                    var parts = exist.Split('/', 2);
                    nodeName = parts[0];
                    addr = parts[1];
                }

                var address = new Address
                {
                    Addr = addr,
                    Name = nodeName
                };

                try
                {
                    await PushPullNodeAsync(address, join: true, cancellationToken);
                    numSuccess++;
                    break; // Successfully joined one node, that's enough
                }
                catch (Exception ex)
                {
                    var err = new Exception($"failed to join {address.Addr}: {ex.Message}", ex);
                    errors.Add(err);
                    _logger?.LogDebug(ex, "Failed to join node at {Address}", address.Addr);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                _logger?.LogWarning(ex, "Failed to join {Address}", exist);
            }
        }

        Exception? finalError = null;
        if (numSuccess == 0 && errors.Count > 0)
        {
            finalError = new AggregateException("Failed to join any nodes", errors);
        }

        return (numSuccess, finalError);
    }

    /// <summary>
    /// Performs a complete state exchange with a specific node over TCP.
    /// </summary>
    private async Task PushPullNodeAsync(Address addr, bool join, CancellationToken cancellationToken = default)
    {
        // Send and receive state
        var (remoteNodes, userState) = await SendAndReceiveStateAsync(addr, join, cancellationToken);

        // Merge remote state into local state
        var stateHandler = new StateHandlers(this, _logger);
        stateHandler.MergeRemoteState(remoteNodes);

        // Handle user state through delegate if present
        if (userState is { Length: > 0 } && Config.Delegate != null)
        {
            try
            {
                Config.Delegate.MergeRemoteState(userState, join: false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to merge remote user state");
            }
        }
    }

    /// <summary>
    /// Initiates a push/pull over a TCP stream with a remote host.
    /// </summary>
    internal async Task<(List<PushNodeState> RemoteNodes, byte[]? UserState)> SendAndReceiveStateAsync(
        Address addr,
        bool join,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(addr.Name) && Config.RequireNodeNames)
        {
            throw new InvalidOperationException("Node names are required");
        }

        // Connect to remote node via TCP
        NetworkStream? conn = null;
        try
        {
            conn = await _transport.DialAddressTimeoutAsync(addr, Config.TCPTimeout, cancellationToken);
            _logger?.LogDebug("Initiating push/pull sync with: {Address}", addr);

            // Add label header to stream if configured (must be the first thing sent)
            if (!string.IsNullOrEmpty(Config.Label))
            {
                await LabelHandler.AddLabelHeaderToStreamAsync(conn, Config.Label, cancellationToken);
            }

            // Send our local state
            await SendLocalStateAsync(conn, join, Config.Label, cancellationToken);

            // Set read deadline
            conn.Socket.ReceiveTimeout = (int)Config.TCPTimeout.TotalMilliseconds;

            // Read response with proper TCP framing
            var buffer = new byte[1];
            var bytesRead = await conn.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed by remote");
            }

            // Read a 4-byte length prefix (always present)
            var lengthBytes = new byte[4];
            lengthBytes[0] = buffer[0];
            var totalRead = 1;
            while (totalRead < 4)
            {
                var read = await conn.ReadAsync(lengthBytes.AsMemory(totalRead, 4 - totalRead), cancellationToken);
                if (read == 0)
                {
                    throw new IOException("Connection closed while reading response length prefix");
                }

                totalRead += read;
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }

            var messageLength = BitConverter.ToInt32(lengthBytes, 0);
            _logger?.LogDebug("Reading {Length} bytes of response data", messageLength);

            // Read exact message payload
            var messageData = new byte[messageLength];
            totalRead = 0;
            while (totalRead < messageLength)
            {
                var read = await conn.ReadAsync(messageData.AsMemory(totalRead, messageLength - totalRead),
                    cancellationToken);
                if (read == 0)
                {
                    throw new IOException(
                        $"Connection closed while reading response ({totalRead}/{messageLength} bytes read)");
                }

                totalRead += read;
            }

            // Decrypt if encryption is enabled
            if (Config.EncryptionEnabled())
            {
                _logger?.LogDebug("Decrypting push-pull response");
                var authData = System.Text.Encoding.UTF8.GetBytes(Config.Label);
                messageData = SecurityTools.DecryptPayload([.. Config.Keyring!.GetKeys()], messageData, authData);
            }

            // Get message type and create stream
            var msgType = (MessageType)messageData[0];
            _logger?.LogDebug("Response message type: {MessageType}", msgType);

            // Handle compression if present
            if (msgType == MessageType.Compress)
            {
                _logger?.LogDebug("Decompressing push-pull response");
                try
                {
                    var compressedData = messageData[1..];
                    var decompressedPayload = Common.CompressionUtils.DecompressPayload(compressedData);

                    if (decompressedPayload.Length > 0)
                    {
                        msgType = (MessageType)decompressedPayload[0];
                        _logger?.LogDebug("Decompressed response message type: {MessageType}", msgType);
                        messageData = decompressedPayload;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to decompress push-pull response");
                    throw;
                }
            }

            Stream responseStream = new MemoryStream(messageData, 1, messageData.Length - 1);

            if (msgType == MessageType.Err)
            {
                // Decode error message
                try
                {
                    var errResp = await MessagePack.MessagePackSerializer.DeserializeAsync<ErrRespMessage>(
                        responseStream, cancellationToken: cancellationToken);
                    throw new RemoteErrorException(errResp.Error, addr.Addr);
                }
                catch (RemoteErrorException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to decode error message from {Address}", addr.Addr);
                    throw new RemoteErrorException("Unknown error (failed to decode error message)", addr.Addr, ex);
                }
            }

            if (msgType != MessageType.PushPull)
            {
                throw new Exception($"Expected PushPull but got {msgType}");
            }

            // Read remote state from the response stream
            var (remoteNodes, userState) = await ReadRemoteStateAsync(responseStream, cancellationToken);
            return (remoteNodes, userState);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Push/pull failed to {Address}", addr.Addr);
            throw;
        }
        finally
        {
            if (conn is not null)
            {
                await conn.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Sends our local state over a TCP stream connection.
    /// </summary>
    private async Task SendLocalStateAsync(NetworkStream conn, bool join, string? streamLabel,
        CancellationToken cancellationToken)
    {
        conn.Socket.SendTimeout = (int)Config.TCPTimeout.TotalMilliseconds;

        // Prepare a local node state - include ALL states (Alive, Suspect, Dead, Left)
        // This matches Go memberlist behavior and is critical for rejoined scenarios.
        // Dead/Left nodes MUST be included, so rejoining nodes can see their own tombstone
        // and trigger refutation by incrementing incarnation.
        List<PushNodeState> localNodes;
        lock (NodeLock)
        {
            localNodes =
            [
                .. Nodes
                    .Select(n => new PushNodeState
                    {
                        Name = n.Name,
                        Addr = n.Node.Addr.GetAddressBytes(),
                        Port = n.Node.Port,
                        Incarnation = n.Incarnation,
                        State = n.State,
                        Meta = n.Node.Meta,
                        Vsn =
                        [
                            n.Node.PMin, n.Node.PMax, n.Node.PCur,
                            n.Node.DMin, n.Node.DMax, n.Node.DCur
                        ]
                    })
            ];
        }

        // Get delegate state if any
        byte[] userData = [];
        if (Config.Delegate != null)
        {
            try
            {
                userData = Config.Delegate.LocalState(join);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to get delegate local state");
            }
        }

        // Encode the payload first (without a message type)
        using var payloadMs = new MemoryStream();

        // Create header
        var header = new Messages.PushPullHeader
        {
            Nodes = localNodes.Count,
            UserStateLen = userData.Length,
            Join = join
        };

        // Encode with MessagePack into the payload stream
        await MessagePack.MessagePackSerializer.SerializeAsync(payloadMs, header, cancellationToken: cancellationToken);
        foreach (var node in localNodes)
        {
            await MessagePack.MessagePackSerializer.SerializeAsync(payloadMs, node,
                cancellationToken: cancellationToken);
        }

        // Append user data
        if (userData.Length > 0)
        {
            await payloadMs.WriteAsync(userData, cancellationToken);
        }

        var payloadBytes = payloadMs.ToArray();

        // Build the message: [PushPull byte][payload]
        // TCP framing (length prefix) is handled by RawSendMsgStreamAsync
        using var finalMs = new MemoryStream();
        finalMs.WriteByte((byte)MessageType.PushPull);
        await finalMs.WriteAsync(payloadBytes, cancellationToken);

        var buffer = finalMs.ToArray();

        // RawSendMsgStreamAsync adds TCP framing and handles compression/encryption
        await RawSendMsgStreamAsync(conn, buffer, streamLabel ?? "", cancellationToken);
    }

    /// <summary>
    /// Reads remote state from a TCP stream connection.
    /// Stream should already be positioned after the message type byte.
    /// </summary>
    private static async Task<(List<PushNodeState> RemoteNodes, byte[]? UserState)> ReadRemoteStateAsync(
        Stream conn,
        CancellationToken cancellationToken)
    {
        // Read header first
        var header = await MessagePack.MessagePackSerializer.DeserializeAsync<PushPullHeader>(
            conn, cancellationToken: cancellationToken);

        // Read nodes
        var remoteNodes = new List<PushNodeState>(header.Nodes);
        for (var i = 0; i < header.Nodes; i++)
        {
            var node = await MessagePack.MessagePackSerializer.DeserializeAsync<PushNodeState>(
                conn, cancellationToken: cancellationToken);
            remoteNodes.Add(node);
        }

        // Read user state if present
        byte[]? userState = null;
        if (header.UserStateLen <= 0) return (remoteNodes, userState);
        userState = new byte[header.UserStateLen];

        var read = await conn.ReadAsync(userState, cancellationToken);
        return read != header.UserStateLen
            ? throw new IOException($"Expected {header.UserStateLen} bytes of user state but got {read}")
            : (remoteNodes, userState);
    }
}