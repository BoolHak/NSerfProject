// Ported from: github.com/hashicorp/memberlist/memberlist.go
// Copyright (c) HashiCorp, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Delegates;
using NSerf.Memberlist.Exceptions;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Memberlist manages cluster membership and member failure detection using a gossip-based protocol.
/// It is eventually consistent but converges quickly. Node failures are detected and network partitions
/// are partially tolerated by attempting to communicate with potentially dead nodes through multiple routes.
/// </summary>
public class Memberlist : IDisposable
{
    // Atomic counters
    private uint _sequenceNum;
    private uint _incarnation;
    internal uint _numNodes;
    private uint _pushPullReq;

    // Advertise address
    private readonly object _advertiseLock = new();
    private IPAddress _advertiseAddr = IPAddress.None;
    private ushort _advertisePort;

    // Configuration and lifecycle
    internal readonly MemberlistConfig _config;
    private int _shutdown; // Atomic boolean
    private readonly CancellationTokenSource _shutdownCts = new();
    private int _leave; // Atomic boolean

    // Transport
    private readonly INodeAwareTransport _transport;
    private readonly PacketHandler _packetHandler;
    private readonly List<Task> _backgroundTasks = new();

    // Node management
    internal readonly object _nodeLock = new();
    internal readonly List<NodeState> _nodes = new();
    internal readonly ConcurrentDictionary<string, NodeState> _nodeMap = new();
    internal readonly ConcurrentDictionary<string, object> _nodeTimers = new(); // Suspicion timers

    // Health awareness
    internal readonly Awareness _awareness;

    // Ack/Nack handlers
    internal readonly ConcurrentDictionary<uint, AckNackHandler> _ackHandlers = new();

    // Broadcast queue
    internal readonly TransmitLimitedQueue _broadcasts;

    // Probe index for round-robin probing
    private int _probeIndex = 0;

    // Logging
    private readonly ILogger? _logger;

    // Local node cache
    private Node? _localNode;

    private bool _disposed;

    private Memberlist(MemberlistConfig config, INodeAwareTransport transport)
    {
        _config = config;
        _transport = transport;
        _logger = config.Logger;
        _incarnation = 0;
        _sequenceNum = 0;
        _numNodes = 1; // Start with just ourselves
        _awareness = new Awareness(config.AwarenessMaxMultiplier);
        _packetHandler = new PacketHandler(this, _logger);

        // Initialize broadcast queue
        _broadcasts = new TransmitLimitedQueue
        {
            NumNodes = () => (int)_numNodes,
            RetransmitMult = config.RetransmitMult
        };
    }

    /// <summary>
    /// Creates a new Memberlist using the given configuration.
    /// This will not connect to any other node yet, but will start all the listeners
    /// to allow other nodes to join this memberlist.
    /// </summary>
    public static async Task<Memberlist> CreateAsync(MemberlistConfig config, CancellationToken cancellationToken = default)
    {
        // Validate protocol version
        if (config.ProtocolVersion < ProtocolVersion.Min)
        {
            throw new ArgumentException(
                $"Protocol version '{config.ProtocolVersion}' too low. Must be in range: [{ProtocolVersion.Min}, {ProtocolVersion.Max}]",
                nameof(config));
        }

        if (config.ProtocolVersion > ProtocolVersion.Max)
        {
            throw new ArgumentException(
                $"Protocol version '{config.ProtocolVersion}' too high. Must be in range: [{ProtocolVersion.Min}, {ProtocolVersion.Max}]",
                nameof(config));
        }

        // Validate node name
        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new ArgumentException("Node name is required", nameof(config));
        }

        // Secret key and keyring are handled through config.Keyring
        // Encryption is applied in RawSendMsgStream when config.EncryptionEnabled() returns true

        // Ensure we have a transport
        if (config.Transport == null)
        {
            throw new ArgumentException("Transport is required", nameof(config));
        }

        // Wrap transport if needed
        INodeAwareTransport transport = config.Transport as INodeAwareTransport ??
            throw new ArgumentException("Transport must implement INodeAwareTransport", nameof(config));

        // Create memberlist
        var memberlist = new Memberlist(config, transport);

        // Initialize local node
        await memberlist.InitializeLocalNodeAsync();

        //Update config with actual bound port if it was 0
        if (config.BindPort == 0 && transport is NetTransport netTransport)
        {
            config.BindPort = netTransport.GetAutoBindPort();
        }

        // Start background listeners
        memberlist.StartBackgroundListeners();

        return memberlist;
    }

    /// <summary>
    /// Gets the local node information.
    /// </summary>
    public Node LocalNode
    {
        get
        {
            if (_localNode == null)
            {
                throw new InvalidOperationException("Local node not initialized");
            }
            return _localNode;
        }
    }

    /// <summary>
    /// Returns the number of members in the cluster from this node's perspective.
    /// </summary>
    public int NumMembers()
    {
        lock (_nodeLock)
        {
            return _nodeMap.Count;
        }
    }

    /// <summary>
    /// Returns the health score of the local node. Lower values are healthier.
    /// 0 is perfectly healthy.
    /// </summary>
    public int GetHealthScore()
    {
        return _awareness.GetHealthScore();
    }

    /// <summary>
    /// Returns the current sequence number.
    /// </summary>
    public uint SequenceNum => Interlocked.CompareExchange(ref _sequenceNum, 0, 0);

    /// <summary>
    /// Returns the estimated number of nodes in the cluster.
    /// </summary>
    internal int EstNumNodes()
    {
        return (int)Interlocked.CompareExchange(ref _numNodes, 0, 0);
    }

    /// <summary>
    /// Returns the number of push/pull requests made.
    /// </summary>
    internal uint PushPullRequests => Interlocked.CompareExchange(ref _pushPullReq, 0, 0);

    /// <summary>
    /// Checks if we're in the leave state.
    /// </summary>
    internal bool IsLeaving => Interlocked.CompareExchange(ref _leave, 0, 0) == 1;

    /// <summary>
    /// Returns the next sequence number atomically.
    /// </summary>
    public uint NextSequenceNum()
    {
        return Interlocked.Increment(ref _sequenceNum);
    }

    /// <summary>
    /// Returns the current incarnation number.
    /// </summary>
    public uint Incarnation => Interlocked.CompareExchange(ref _incarnation, 0, 0);

    /// <summary>
    /// Increments and returns the incarnation number atomically.
    /// </summary>
    public uint NextIncarnation()
    {
        return Interlocked.Increment(ref _incarnation);
    }

    /// <summary>
    /// Skips the incarnation number by the given offset.
    /// Returns the new incarnation number.
    /// </summary>
    internal uint SkipIncarnation(uint offset)
    {
        return Interlocked.Add(ref _incarnation, offset);
    }

    /// <summary>
    /// Gets the advertise address and port.
    /// </summary>
    internal (IPAddress Address, int Port) GetAdvertiseAddr()
    {
        lock (_advertiseLock)
        {
            return (_advertiseAddr, _advertisePort);
        }
    }

    /// <summary>
    /// Initiates a graceful shutdown of the memberlist.
    /// </summary>
    public async Task ShutdownAsync()
    {
        // Check if already shutdown
        if (Interlocked.CompareExchange(ref _shutdown, 1, 0) == 1)
        {
            // Already shutdown
            return;
        }

        _logger?.LogInformation("Memberlist: Shutting down");

        // Signal shutdown
        _shutdownCts.Cancel();

        // Wait for background tasks to complete
        try
        {
            await Task.WhenAll(tasks: [.. _backgroundTasks]);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error waiting background tasks");
        }

        // Close transport
        try
        {
            await _transport.ShutdownAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Error shutting down transport");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Starts background listeners for the transport to ingest packets and streams.
    /// </summary>
    private void StartBackgroundListeners()
    {
        // UDP packet listener
        var packetTask = Task.Run(async () =>
        {
            try
            {
                var reader = _transport.PacketChannel;
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        if (!await reader.WaitToReadAsync(_shutdownCts.Token)) break;
                        while (reader.TryRead(out var p))
                        {
                            _packetHandler.IngestPacket(p.Buf, p.From, p.Timestamp);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Packet listener error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Packet listener crashed");
            }
        }, _shutdownCts.Token);
        _backgroundTasks.Add(packetTask);

        // TCP stream listener
        var streamTask = Task.Run(async () =>
        {
            try
            {
                var reader = _transport.StreamChannel;
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        if (!await reader.WaitToReadAsync(_shutdownCts.Token)) break;
                        while (reader.TryRead(out var stream))
                        {
                            // Handle stream connection in background
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await HandleStreamAsync(stream);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "Stream handler error");
                                }
                                finally
                                {
                                    stream?.Dispose();
                                }
                            }, _shutdownCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Stream listener error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Stream listener crashed");
            }
        }, _shutdownCts.Token);
        _backgroundTasks.Add(streamTask);

        // Gossip scheduler - periodically sends queued broadcasts to random nodes
        var gossipTask = Task.Run(async () =>
        {
            try
            {
                while (!_shutdownCts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_config.GossipInterval, _shutdownCts.Token);
                        await GossipAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Gossip scheduler error");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Gossip scheduler crashed");
            }
        }, _shutdownCts.Token);
        _backgroundTasks.Add(gossipTask);

        // Probe scheduler - periodically probes nodes for failure detection
        if (_config.ProbeInterval > TimeSpan.Zero)
        {
            var probeTask = Task.Run(async () =>
            {
                try
                {
                    // Add initial random stagger to avoid synchronization
                    var stagger = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)_config.ProbeInterval.TotalMilliseconds));
                    await Task.Delay(stagger, _shutdownCts.Token);

                    while (!_shutdownCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(_config.ProbeInterval, _shutdownCts.Token);
                            await ProbeAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Probe scheduler error");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Probe scheduler crashed");
                }
            }, _shutdownCts.Token);
            _backgroundTasks.Add(probeTask);
        }
    }

    /// <summary>
    /// Performs one round of failure detection by probing a node.
    /// This is called periodically by the probe scheduler.
    /// </summary>
    private async Task ProbeAsync()
    {
        // Skip probing if we're leaving the cluster
        if (IsLeaving)
        {
            return;
        }

        int numCheck = 0;

        while (true)
        {
            NodeState? nodeToProbe = null;

            lock (_nodeLock)
            {
                // Make sure we don't wrap around infinitely
                if (numCheck >= _nodes.Count)
                {
                    return; // No nodes to probe
                }

                // Handle wrap around
                if (_probeIndex >= _nodes.Count)
                {
                    _probeIndex = 0;
                    numCheck++;
                    continue;
                }

                // Get candidate node
                var node = _nodes[_probeIndex];
                _probeIndex++;

                // Skip local node
                if (node.Name == _config.Name)
                {
                    numCheck++;
                    continue;
                }

                // Skip dead or left nodes
                if (node.State == NodeStateType.Dead || node.State == NodeStateType.Left)
                {
                    numCheck++;
                    continue;
                }

                nodeToProbe = node;
            }

            // Found a node to probe
            if (nodeToProbe != null)
            {
                await ProbeNodeAsync(nodeToProbe);
                return;
            }
        }
    }

    /// <summary>
    /// Probes a specific node to check if it's alive using UDP ping.
    /// </summary>
    private async Task ProbeNodeAsync(NodeState node)
    {
        _logger?.LogDebug("Probing node: {Node}", node.Name);

        // Get sequence number for this probe
        var seqNo = NextSequenceNum();

        // Create ping message
        var ping = new Messages.PingMessage
        {
            SeqNo = seqNo,
            Node = node.Name
        };

        // Setup ack handler to wait for response
        var ackReceived = new TaskCompletionSource<bool>();
        var handler = new AckNackHandler(_logger);

        handler.SetAckHandler(
            seqNo,
            (payload, timestamp) =>
            {
                ackReceived.TrySetResult(true); // Success
            },
            () =>
            {
                ackReceived.TrySetResult(false); // Timeout/Nack
            },
            _config.ProbeTimeout
        );

        _ackHandlers[seqNo] = handler;

        try
        {
            // Encode ping using MessagePack like Go implementation
            var (advertiseAddr, advertisePort) = GetAdvertiseAddr();
            var pingMsg = new Messages.PingMessage
            {
                SeqNo = seqNo,
                Node = node.Name,  // Target node name for verification
                SourceAddr = advertiseAddr.GetAddressBytes(),
                SourcePort = (ushort)advertisePort,
                SourceNode = _config.Name
            };

            var pingBytes = Messages.MessageEncoder.Encode(Messages.MessageType.Ping, pingMsg);
            var addr = new Address
            {
                Addr = $"{node.Node.Addr}:{node.Node.Port}",
                Name = node.Name
            };

            await SendPacketAsync(pingBytes, addr, _shutdownCts.Token);

            // Start TCP fallback in parallel if enabled and node supports it
            // Go implementation does this for protocol version >= 3
            var tcpFallbackTask = Task.FromResult(false);
            var disableTcpPings = _config.DisableTcpPings ||
                (_config.DisableTcpPingsForNode != null && _config.DisableTcpPingsForNode(node.Name));

            if (!disableTcpPings && node.Node.PMax >= 3)
            {
                tcpFallbackTask = Task.Run(async () =>
                {
                    try
                    {
                        var deadline = DateTimeOffset.UtcNow.Add(_config.ProbeTimeout);
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
                if (_nodeTimers.TryRemove(node.Name, out var timerObj))
                {
                    if (timerObj is Suspicion suspicion)
                    {
                        suspicion.Dispose();
                    }
                }
                return;
            }

            // UDP failed, check TCP fallback result
            var tcpSuccess = await tcpFallbackTask;
            if (tcpSuccess)
            {
                _logger?.LogWarning("Was able to connect to {Node} over TCP but UDP probes failed, network may be misconfigured", node.Name);

                // Cancel any suspicion timer
                if (_nodeTimers.TryRemove(node.Name, out var timerObj))
                {
                    if (timerObj is Suspicion suspicion)
                    {
                        suspicion.Dispose();
                    }
                }
                return;
            }

            // Both UDP and TCP failed - mark as suspect
            // Don't mark nodes as suspect if we're leaving
            if (IsLeaving)
            {
                return;
            }

            _logger?.LogWarning("Probe failed for node: {Node}, marking as suspect", node.Name);

            // Mark node as suspect
            var suspect = new Messages.Suspect
            {
                Incarnation = node.Incarnation,
                Node = node.Name,
                From = _config.Name
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
            // Cleanup
            _ackHandlers.TryRemove(seqNo, out _);
        }
    }

    /// <summary>
    /// Gossip is invoked every GossipInterval to broadcast our gossip messages
    /// to a few random nodes.
    /// </summary>
    private async Task GossipAsync()
    {
        // Get some random live, suspect, or recently dead nodes
        List<Node> kNodes;
        lock (_nodeLock)
        {
            kNodes = NodeStateManager.KRandomNodes(_config.GossipNodes, _nodes, node =>
            {
                // Exclude self
                if (node.Name == _config.Name)
                    return true;

                // Include alive and suspect nodes
                switch (node.State)
                {
                    case NodeStateType.Alive:
                    case NodeStateType.Suspect:
                        return false;

                    case NodeStateType.Dead:
                        // Only gossip to dead nodes if they died recently
                        return (DateTimeOffset.UtcNow - node.StateChange) > _config.GossipToTheDeadTime;

                    default:
                        return true; // Exclude left nodes
                }
            });
        }

        if (kNodes.Count == 0)
        {
            return;
        }

        // Calculate bytes available for broadcasts
        int bytesAvail = _config.UDPBufferSize - Messages.MessageConstants.CompoundHeaderOverhead;

        Console.WriteLine($"[GOSSIP] Starting gossip round to {kNodes.Count} nodes, {_broadcasts.NumQueued()} broadcasts queued");
        _logger?.LogDebug("[GOSSIP] Starting gossip round to {Count} nodes, {Queued} broadcasts queued", kNodes.Count, _broadcasts.NumQueued());

        foreach (var node in kNodes)
        {
            // Get pending broadcasts
            var msgs = _broadcasts.GetBroadcasts(Messages.MessageConstants.CompoundOverhead, bytesAvail);
            if (msgs.Count == 0)
            {
                _logger?.LogDebug("[GOSSIP] No more broadcasts to send");
                return; // No more broadcasts to send
            }

            _logger?.LogDebug("[GOSSIP] Got {Count} broadcasts to send to {Node}", msgs.Count, node.Name);

            var addr = new Transport.Address
            {
                Addr = $"{node.Addr}:{node.Port}",
                Name = node.Name
            };

            Console.WriteLine($"[GOSSIP] Sending to {node.Name} at {addr.Addr}");

            try
            {
                if (msgs.Count == 1)
                {
                    // Send single message as-is
                    Console.WriteLine($"[GOSSIP] Sending single message of {msgs[0].Length} bytes, first byte: {msgs[0][0]}");
                    var packet = msgs[0];

                    // Add label header if configured
                    if (!string.IsNullOrEmpty(_config.Label))
                    {
                        packet = LabelHandler.AddLabelHeaderToPacket(packet, _config.Label);
                    }

                    await _transport.WriteToAddressAsync(packet, addr, _shutdownCts.Token);
                    Console.WriteLine($"[GOSSIP] WriteToAddressAsync returned");
                }
                else
                {
                    // Create compound message
                    var compoundBytes = Messages.CompoundMessage.MakeCompoundMessage(msgs);
                    Console.WriteLine($"[GOSSIP] Sending compound message of {compoundBytes.Length} bytes");

                    // Add label header if configured
                    if (!string.IsNullOrEmpty(_config.Label))
                    {
                        compoundBytes = LabelHandler.AddLabelHeaderToPacket(compoundBytes, _config.Label);
                    }

                    await _transport.WriteToAddressAsync(compoundBytes, addr, _shutdownCts.Token);
                }

                Console.WriteLine($"[GOSSIP] Send complete to {node.Name}");
                _logger?.LogDebug("[GOSSIP] Successfully sent {Count} messages to {Node} at {Addr}", msgs.Count, node.Name, addr.Addr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GOSSIP] Exception sending to {node.Name}: {ex.Message}");
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
            stream.Socket.ReceiveTimeout = (int)_config.TCPTimeout.TotalMilliseconds;
            stream.Socket.SendTimeout = (int)_config.TCPTimeout.TotalMilliseconds;

            // Remove and validate label header from stream
            var (streamLabel, peekedData) = await LabelHandler.RemoveLabelHeaderFromStreamAsync(stream, _shutdownCts.Token);

            // Validate label
            if (_config.SkipInboundLabelCheck)
            {
                if (!string.IsNullOrEmpty(streamLabel))
                {
                    _logger?.LogError("Unexpected double stream label header");
                    return;
                }
                // Set this from config so that the auth data assertions work below
                streamLabel = _config.Label;
            }

            if (_config.Label != streamLabel)
            {
                _logger?.LogError("Discarding stream with unacceptable label \"{Label}\"", streamLabel);
                return;
            }

            // Read message type (either from peeked data or from stream)
            byte[] buffer;
            if (peekedData != null && peekedData.Length > 0)
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

            var msgType = (MessageType)buffer[0];
            _logger?.LogDebug("Received stream message type: {MessageType}", msgType);

            if (msgType == MessageType.PushPull)
            {
                await HandlePushPullStreamAsync(stream);
            }
            else if (msgType == MessageType.User)
            {
                await HandleUserMsgStreamAsync(stream);
            }
            else
            {
                _logger?.LogWarning("Unknown stream message type: {MessageType}", msgType);
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
    private async Task HandleUserMsgStreamAsync(NetworkStream stream)
    {
        try
        {
            // Read the user message header using MessagePack deserialization
            var header = await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.UserMsgHeader>(stream, cancellationToken: _shutdownCts.Token);

            _logger?.LogDebug("User message header: {Length} bytes", header.UserMsgLen);

            // Read the user message payload (raw bytes, not MessagePack)
            if (header.UserMsgLen > 0)
            {
                var userMsgBytes = new byte[header.UserMsgLen];
                var totalRead = 0;

                while (totalRead < header.UserMsgLen)
                {
                    var bytesRead = await stream.ReadAsync(userMsgBytes.AsMemory(totalRead, header.UserMsgLen - totalRead), _shutdownCts.Token);
                    if (bytesRead == 0)
                    {
                        throw new IOException("Connection closed while reading user message");
                    }
                    totalRead += bytesRead;
                }

                // Pass to delegate
                if (_config.Delegate != null)
                {
                    _config.Delegate.NotifyMsg(userMsgBytes);
                    _logger?.LogDebug("User message ({Size} bytes) delivered to delegate via TCP", userMsgBytes.Length);
                }
                else
                {
                    _logger?.LogDebug("User message ({Size} bytes) received via TCP but no delegate configured", userMsgBytes.Length);
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
    private async Task HandlePushPullStreamAsync(NetworkStream stream)
    {
        try
        {
            // Read remote state  
            var (remoteNodes, userState) = await ReadRemoteStateAsync(stream, _shutdownCts.Token);
            _logger?.LogDebug("Received {Count} nodes from remote", remoteNodes.Count);

            // Merge remote state
            var stateHandler = new StateHandlers(this, _logger);
            stateHandler.MergeRemoteState(remoteNodes);

            // Handle user state through delegate if needed
            if (userState != null && userState.Length > 0 && _config.Delegate != null)
            {
                try
                {
                    _config.Delegate.MergeRemoteState(userState, join: false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to merge remote user state");
                }
            }

            // Send our state back
            await SendLocalStateAsync(stream, join: false, _config.Label, _shutdownCts.Token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Push/pull stream handler error");

            // Try to send error response
            try
            {
                await stream.WriteAsync(new byte[] { (byte)MessageType.Err });
                await stream.FlushAsync();
            }
            catch
            {
                // Ignore errors when sending error response
            }
        }
    }

    /// <summary>
    /// Sends a raw UDP packet to a given Address via the transport, adding label header if configured.
    /// </summary>
    internal async Task SendPacketAsync(byte[] buffer, Transport.Address addr, CancellationToken cancellationToken = default)
    {
        // Add label header if configured
        if (!string.IsNullOrEmpty(_config.Label))
        {
            buffer = LabelHandler.AddLabelHeaderToPacket(buffer, _config.Label);
        }

        await _transport.WriteToAddressAsync(buffer, addr, cancellationToken);
    }

    /// <summary>
    /// Sends a UDP packet to an address, adding label header if configured.
    /// </summary>
    internal async Task SendUdpAsync(byte[] buffer, Address addr, CancellationToken cancellationToken = default)
    {
        // Add label header if configured
        if (!string.IsNullOrEmpty(_config.Label))
        {
            buffer = LabelHandler.AddLabelHeaderToPacket(buffer, _config.Label);
        }

        await _transport.WriteToAddressAsync(buffer, addr, cancellationToken);
    }

    /// <summary>
    /// Initializes the local node with address from transport.
    /// </summary>
    private async Task InitializeLocalNodeAsync()
    {
        // Get advertise address from transport
        var (ip, port) = _transport.FinalAdvertiseAddr(_config.AdvertiseAddr, _config.AdvertisePort);

        lock (_advertiseLock)
        {
            _advertiseAddr = ip;
            _advertisePort = (ushort)port;
        }

        // Create local node
        _localNode = new Node
        {
            Name = _config.Name,
            Addr = ip,
            Port = (ushort)port,
            Meta = _config.Delegate?.NodeMeta(ushort.MaxValue) ?? Array.Empty<byte>(),
            State = NodeStateType.Alive,
            PMin = ProtocolVersion.Min,
            PMax = ProtocolVersion.Max,
            PCur = _config.ProtocolVersion,
            DMin = _config.DelegateProtocolMin,
            DMax = _config.DelegateProtocolMax,
            DCur = _config.DelegateProtocolVersion
        };

        // Add ourselves to the node map
        var localState = new NodeState
        {
            Node = _localNode,
            State = NodeStateType.Alive,
            StateChange = DateTimeOffset.UtcNow,
            Incarnation = 0
        };

        _nodeMap[_config.Name] = localState;

        lock (_nodeLock)
        {
            _nodes.Add(localState);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Sends a raw message over a stream, applying compression and encryption if enabled.
    /// </summary>
    internal async Task RawSendMsgStreamAsync(NetworkStream conn, byte[] buf, string streamLabel, CancellationToken cancellationToken = default)
    {
        // Note: Label header should be added once when the connection is created (in transport layer),
        // not for every message sent on the stream.

        // Apply compression if enabled
        if (_config.EnableCompression)
        {
            try
            {
                var compressed = Common.CompressionUtils.CompressPayload(buf);
                buf = Messages.MessageEncoder.Encode(Messages.MessageType.Compress, compressed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to compress payload");
            }
        }

        // Apply encryption if enabled
        if (_config.EncryptionEnabled() && _config.GossipVerifyOutgoing)
        {
            try
            {
                var authData = System.Text.Encoding.UTF8.GetBytes(streamLabel);
                var primaryKey = _config.Keyring!.GetPrimaryKey() ?? throw new InvalidOperationException("No primary key available");
                buf = Security.EncryptPayload(1, primaryKey, buf, authData);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to encrypt payload");
                throw;
            }
        }

        // Write the full buffer to the stream
        await conn.WriteAsync(buf, cancellationToken);
        await conn.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a ping and waits for an ack over a TCP stream.
    /// </summary>
    internal async Task<bool> SendPingAndWaitForAckAsync(Address addr, Messages.PingMessage ping, DateTimeOffset deadline, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(addr.Name) && _config.RequireNodeNames)
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
            if (!string.IsNullOrEmpty(_config.Label))
            {
                await LabelHandler.AddLabelHeaderToStreamAsync(conn, _config.Label, cts.Token);
            }

            // Encode the ping message
            var pingBytes = Messages.MessageEncoder.Encode(Messages.MessageType.Ping, ping);

            // Send the ping
            await RawSendMsgStreamAsync(conn, pingBytes, _config.Label, cts.Token);

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

            if (ack.SeqNo != ping.SeqNo)
            {
                _logger?.LogWarning("Sequence number mismatch: expected {Expected}, got {Actual}", ping.SeqNo, ack.SeqNo);
                return false;
            }

            return true;
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
            conn?.Dispose();
        }
    }

    /// <summary>
    /// Encodes a message and queues it for broadcast to the cluster.
    /// </summary>
    internal void EncodeAndBroadcast(string node, MessageType msgType, object message)
    {
        Console.WriteLine($"[ENCODE] EncodeAndBroadcast called for {node}, type {msgType}");
        EncodeBroadcastNotify(node, msgType, message, null);
    }

    /// <summary>
    /// Encodes a message and queues it for broadcast with notification when complete.
    /// </summary>
    internal void EncodeBroadcastNotify(string node, MessageType msgType, object message, BroadcastNotifyChannel? notify)
    {
        Console.WriteLine($"[ENCODE] EncodeBroadcastNotify called for {node}, type {msgType}, message type: {message.GetType().Name}");
        try
        {
            // Convert protocol structures to MessagePack messages
            object msgToEncode = message;
            if (message is Messages.Alive alive)
            {
                msgToEncode = new Messages.AliveMessage
                {
                    Incarnation = alive.Incarnation,
                    Node = alive.Node ?? string.Empty,
                    Addr = alive.Addr ?? [],
                    Port = alive.Port,
                    Meta = alive.Meta ?? [],
                    Vsn = alive.Vsn ?? [],
                };
            }
            else if (message is Messages.Suspect suspect)
            {
                msgToEncode = new Messages.SuspectMessage
                {
                    Incarnation = suspect.Incarnation,
                    Node = suspect.Node,
                    From = suspect.From
                };
            }
            else if (message is Messages.Dead dead)
            {
                msgToEncode = new Messages.DeadMessage
                {
                    Incarnation = dead.Incarnation,
                    Node = dead.Node,
                    From = dead.From
                };
            }

            Console.WriteLine($"[ENCODE] About to encode {msgToEncode.GetType().Name}...");
            var encoded = Messages.MessageEncoder.Encode(msgType, msgToEncode);
            Console.WriteLine($"[ENCODE] Encoded message size: {encoded.Length} bytes");

            // Check size limits
            if (encoded.Length > _config.UDPBufferSize)
            {
                Console.WriteLine($"[ENCODE] Message too large! {encoded.Length} > {_config.UDPBufferSize}");
                _logger?.LogError("Encoded {Type} message for {Node} is too large ({Size} > {Max})",
                    msgType, node, encoded.Length, _config.UDPBufferSize);
                return;
            }

            // Check if we should skip broadcasting our own messages based on config
            if (node == _config.Name && _config.DeadNodeReclaimTime == TimeSpan.Zero)
            {
                Console.WriteLine($"[ENCODE] Skipping broadcast for local node (reclaim time is 0)");
                // Don't broadcast our own state changes if reclaim time is 0
                _logger?.LogDebug("Skipping broadcast for local node {Node} (reclaim time is 0)", node);
                return;
            }

            Console.WriteLine($"[ENCODE] Calling QueueBroadcast");
            QueueBroadcast(node, msgType, encoded, notify);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENCODE] Exception: {ex.Message}");
            _logger?.LogError(ex, "Failed to encode and broadcast {Type} for {Node}", msgType, node);
        }
    }

    /// <summary>
    /// Returns a list of all known live nodes.
    /// The node structures returned must not be modified.
    /// </summary>
    public List<Node> Members()
    {
        lock (_nodeLock)
        {
            return [.. _nodes
                .Where(n => n.State != NodeStateType.Dead && n.State != NodeStateType.Left)
                .Select(n => n.Node)];
        }
    }

    /// <summary>
    /// Leave will broadcast a leave message but will not shutdown the background
    /// listeners, meaning the node will continue participating in gossip and state
    /// updates.
    /// This method is safe to call multiple times.
    /// </summary>
    public async Task<Exception?> LeaveAsync(TimeSpan timeout)
    {
        // Set leave flag to prevent gossiping Alive messages for ourselves
        if (Interlocked.CompareExchange(ref _leave, 1, 0) != 0)
        {
            // Already left
            return null;
        }

        // Cancel all suspicion timers to prevent marking healthy nodes as dead
        foreach (var kvp in _nodeTimers)
        {
            if (kvp.Value is Suspicion suspicion)
            {
                suspicion.Dispose();
            }
        }
        _nodeTimers.Clear();

        // Update our own state to Left
        lock (_nodeLock)
        {
            if (_nodeMap.TryGetValue(_config.Name, out var ourState))
            {
                ourState.State = NodeStateType.Left;
                ourState.StateChange = DateTimeOffset.UtcNow;
            }
        }

        var leaveManager = new LeaveManager(this, _logger);
        var result = await leaveManager.LeaveAsync(_config.Name, timeout);

        return result.Success ? null : result.Error;
    }

    /// <summary>
    /// Join is used to take an existing Memberlist and attempt to join a cluster
    /// by contacting all the given hosts and performing a state sync.
    /// Returns the number of nodes successfully contacted and an error if none could be reached.
    /// </summary>
    public async Task<(int NumJoined, Exception? Error)> JoinAsync(IEnumerable<string> existing, CancellationToken cancellationToken = default)
    {
        int numSuccess = 0;
        var errors = new List<Exception>();

        foreach (var exist in existing)
        {
            try
            {
                // Parse address directly - it should be in "host:port" format
                var address = new Address
                {
                    Addr = exist,
                    Name = "" // Node name is optional for join
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
    internal async Task PushPullNodeAsync(Address addr, bool join, CancellationToken cancellationToken = default)
    {
        // Send and receive state
        var (remoteNodes, userState) = await SendAndReceiveStateAsync(addr, join, cancellationToken);

        // Merge remote state into local state
        var stateHandler = new StateHandlers(this, _logger);
        stateHandler.MergeRemoteState(remoteNodes);

        // Handle user state through delegate if present
        if (userState != null && userState.Length > 0 && _config.Delegate != null)
        {
            try
            {
                _config.Delegate.MergeRemoteState(userState, join: false);
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
    private async Task<(List<Messages.PushNodeState> RemoteNodes, byte[]? UserState)> SendAndReceiveStateAsync(
        Address addr,
        bool join,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(addr.Name) && _config.RequireNodeNames)
        {
            throw new InvalidOperationException("Node names are required");
        }

        // Connect to remote node via TCP
        NetworkStream? conn = null;
        try
        {
            conn = await _transport.DialAddressTimeoutAsync(addr, _config.TCPTimeout, cancellationToken);
            _logger?.LogDebug("Initiating push/pull sync with: {Address}", addr);

            // Add label header to stream if configured (must be first thing sent)
            if (!string.IsNullOrEmpty(_config.Label))
            {
                await LabelHandler.AddLabelHeaderToStreamAsync(conn, _config.Label, cancellationToken);
            }

            // Send our local state
            await SendLocalStateAsync(conn, join, _config.Label, cancellationToken);

            // Set read deadline
            conn.Socket.ReceiveTimeout = (int)_config.TCPTimeout.TotalMilliseconds;

            // Read response
            var buffer = new byte[1];
            var bytesRead = await conn.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed by remote");
            }

            var msgType = (MessageType)buffer[0];

            if (msgType == MessageType.Err)
            {
                // Decode error message from remote
                try
                {
                    var errResp = await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.ErrRespMessage>(
                        conn, cancellationToken: cancellationToken);
                    throw new RemoteErrorException(errResp.Error, addr.Addr);
                }
                catch (RemoteErrorException)
                {
                    throw; // Re-throw remote errors
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

            // Read remote state
            var (remoteNodes, userState) = await ReadRemoteStateAsync(conn, cancellationToken);
            return (remoteNodes, userState);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Push/pull failed to {Address}", addr.Addr);
            throw;
        }
        finally
        {
            conn?.Dispose();
        }
    }

    /// <summary>
    /// Sends our local state over a TCP stream connection.
    /// </summary>
    private async Task SendLocalStateAsync(NetworkStream conn, bool join, string? streamLabel, CancellationToken cancellationToken)
    {
        conn.Socket.SendTimeout = (int)_config.TCPTimeout.TotalMilliseconds;

        // Prepare local node state (only include alive/suspect nodes, not dead)
        List<Messages.PushNodeState> localNodes;
        lock (_nodeLock)
        {
            localNodes = [.. _nodes
                .Where(n => n.State != NodeStateType.Dead) // Don't send dead nodes
                .Select(n => new Messages.PushNodeState
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
                })];
        }

        // Get delegate state if any
        byte[] userData = [];
        if (_config.Delegate != null)
        {
            try
            {
                userData = _config.Delegate.LocalState(join);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to get delegate local state");
            }
        }

        // Encode the payload first (without message type)
        using var payloadMs = new MemoryStream();

        // Create header
        var header = new Messages.PushPullHeader
        {
            Nodes = localNodes.Count,
            UserStateLen = userData.Length,
            Join = join
        };

        // Encode with MessagePack into payload stream
        await MessagePack.MessagePackSerializer.SerializeAsync(payloadMs, header, cancellationToken: cancellationToken);
        foreach (var node in localNodes)
        {
            await MessagePack.MessagePackSerializer.SerializeAsync(payloadMs, node, cancellationToken: cancellationToken);
        }

        // Append user data
        if (userData.Length > 0)
        {
            await payloadMs.WriteAsync(userData, cancellationToken);
        }

        var payloadBytes = payloadMs.ToArray();

        // Now build the complete message with length prefix
        using var finalMs = new MemoryStream();

        // Write message type
        finalMs.WriteByte((byte)MessageType.PushPull);

        // Write length prefix (4 bytes, big-endian)
        var lengthBytes = BitConverter.GetBytes(payloadBytes.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(lengthBytes);
        }
        await finalMs.WriteAsync(lengthBytes, cancellationToken);

        // Write payload
        await finalMs.WriteAsync(payloadBytes, cancellationToken);

        // Send the complete buffer
        var buffer = finalMs.ToArray();
        await conn.WriteAsync(buffer, cancellationToken);
        await conn.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads remote state from a TCP stream connection.
    /// </summary>
    private async Task<(List<Messages.PushNodeState> RemoteNodes, byte[]? UserState)> ReadRemoteStateAsync(
        NetworkStream conn,
        CancellationToken cancellationToken)
    {
        // Read length prefix (4 bytes, big-endian)
        var lengthBytes = new byte[4];
        var totalRead = 0;
        while (totalRead < 4)
        {
            var bytesRead = await conn.ReadAsync(lengthBytes.AsMemory(totalRead, 4 - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading length prefix");
            }
            totalRead += bytesRead;
        }

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(lengthBytes);
        }
        var payloadLength = BitConverter.ToInt32(lengthBytes, 0);

        // Read exact payload bytes
        var payloadBuffer = new byte[payloadLength];
        totalRead = 0;
        while (totalRead < payloadLength)
        {
            var bytesRead = await conn.ReadAsync(payloadBuffer.AsMemory(totalRead, payloadLength - totalRead), cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading payload");
            }
            totalRead += bytesRead;
        }

        // Now deserialize from the complete buffer
        using var payloadStream = new MemoryStream(payloadBuffer, false);

        // Read header
        var header = await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.PushPullHeader>(payloadStream, cancellationToken: cancellationToken);

        // Read nodes
        var remoteNodes = new List<Messages.PushNodeState>(header.Nodes);
        for (int i = 0; i < header.Nodes; i++)
        {
            var node = await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.PushNodeState>(payloadStream, cancellationToken: cancellationToken);
            remoteNodes.Add(node);
        }

        // Read user state if present
        byte[]? userState = null;
        if (header.UserStateLen > 0)
        {
            userState = new byte[header.UserStateLen];
            var read = await payloadStream.ReadAsync(userState, cancellationToken);
            if (read != header.UserStateLen)
            {
                throw new IOException($"Expected {header.UserStateLen} bytes of user state but got {read}");
            }
        }

        return (remoteNodes, userState);
    }

    /// <summary>
    /// Queues a broadcast for dissemination to the cluster.
    /// </summary>
    private void QueueBroadcast(string node, MessageType msgType, byte[] message, BroadcastNotifyChannel? notify)
    {
        Console.WriteLine($"[QUEUEBCAST] QueueBroadcast CALLED for {node}, type {msgType}, message size {message.Length}");

        // Create broadcast with notify channel - it will call notify.Notify() when Finished() is invoked
        // by the TransmitLimitedQueue (either when invalidated or transmit limit reached)
        IBroadcast broadcast = msgType switch
        {
            MessageType.Alive => new AliveMessageBroadcast(node, message, notify),
            MessageType.Suspect => new SuspectMessageBroadcast(node, message, notify),
            MessageType.Dead => new DeadMessageBroadcast(node, message, notify),
            _ => throw new ArgumentException($"Unsupported message type for broadcast: {msgType}", nameof(msgType))
        };

        Console.WriteLine($"[QUEUEBCAST] Created broadcast object, about to queue...");
        _broadcasts.QueueBroadcast(broadcast);
        Console.WriteLine($"[QUEUEBCAST] QUEUED! Queue size now: {_broadcasts.NumQueued()}");
        Console.WriteLine($"[BROADCAST] Queued {msgType} broadcast for {node}, queue size: {_broadcasts.NumQueued()}");
        _logger?.LogDebug("[BROADCAST] Queued {Type} broadcast for {Node}, queue size: {Size}", msgType, node, _broadcasts.NumQueued());
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            ShutdownAsync().GetAwaiter().GetResult();
            _shutdownCts.Dispose();
        }
    }
}
