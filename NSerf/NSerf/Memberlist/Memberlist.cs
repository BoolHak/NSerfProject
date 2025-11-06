// Ported from: github.com/hashicorp/memberlist/memberlist.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Exceptions;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Memberlist manages cluster membership and member failure detection using a gossip-based protocol.
/// It is eventually consistent but converges quickly. Node failures are detected, and network partitions
/// are partially tolerated by attempting to communicate with potentially dead nodes through multiple routes.
/// </summary>
public class Memberlist : IDisposable, IAsyncDisposable
{
    // Atomic counters
    private uint _sequenceNum;
    private uint _incarnation;
    internal uint NumNodes;
    private uint _pushPullReq;

    // Advertise address
    private readonly object _advertiseLock = new();
    private IPAddress _advertiseAddr = IPAddress.None;
    private ushort _advertisePort;

    // Configuration and lifecycle
    internal readonly MemberlistConfig Config;
    private int _shutdown; // Atomic boolean
    private readonly CancellationTokenSource _shutdownCts = new();
    private int _leave; // Atomic boolean

    // Transport
    private readonly INodeAwareTransport _transport;
    private readonly PacketHandler _packetHandler;
    private readonly List<Task> _backgroundTasks = [];

    // Node management
    internal readonly object NodeLock = new();
    internal readonly List<NodeState> Nodes = [];
    internal readonly ConcurrentDictionary<string, NodeState> NodeMap = new();
    internal readonly ConcurrentDictionary<string, object> NodeTimers = new(); // Suspicion timers

    // Health awareness
    internal readonly Awareness Awareness;

    // Ack/Nack handlers
    internal readonly ConcurrentDictionary<uint, AckNackHandler> AckHandlers = new();

    // Broadcast queue
    internal readonly TransmitLimitedQueue Broadcasts;

    // Push-pull state synchronization
    internal readonly PushPullManager PushPullManager;

    // Probe index for round-robin probing
    private int _probeIndex = 0;

    // Logging
    private readonly ILogger? _logger;

    // Local node cache
    private Node? _localNode;

    private bool _disposed;

    private Memberlist(MemberlistConfig config, INodeAwareTransport transport)
    {
        Config = config;
        _transport = transport;
        _logger = config.Logger;
        _incarnation = 0;
        _sequenceNum = 0;
        NumNodes = 1; // Start with just ourselves
        Awareness = new Awareness(config.AwarenessMaxMultiplier);
        _packetHandler = new PacketHandler(this, _logger);
        PushPullManager = new PushPullManager(_logger);

        // Initialize broadcast queue
        Broadcasts = new TransmitLimitedQueue
        {
            NumNodes = () => (int)NumNodes,
            RetransmitMult = config.RetransmitMult
        };
    }

    /// <summary>
    /// Join a variant that accepts explicit node names with addresses.
    /// Use this when RequireNodeNames is true so the remote can validate our identity.
    /// </summary>
    public async Task<(int NumJoined, Exception? Error)> JoinWithNamedAddressesAsync(IEnumerable<(string Name, string Addr)> existing, CancellationToken cancellationToken = default)
    {
        var numSuccess = 0;
        var errors = new List<Exception>();

        foreach (var (name, addr) in existing)
        {
            try
            {
                var address = new Address
                {
                    Addr = addr,
                    Name = name
                };

                try
                {
                    await PushPullNodeAsync(address, join: true, cancellationToken);
                    numSuccess++;
                    break; // Successfully joined one node, that's enough
                }
                catch (Exception ex)
                {
                    var err = new Exception($"failed to join {address.Name}@{address.Addr}: {ex.Message}", ex);
                    errors.Add(err);
                    _logger?.LogDebug(ex, "Failed to join node at {Address}", $"{address.Name}@{address.Addr}");
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
                _logger?.LogWarning(ex, "Failed to join {Address}", $"{name}@{addr}");
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
    /// Creates a new Memberlist using the given configuration.
    /// This will not connect to any other node yet, but will start all the listeners
    /// to allow other nodes to join this memberlist.
    /// </summary>
    public static Memberlist Create(MemberlistConfig config)
    {
        switch (config.ProtocolVersion)
        {
            // Validate protocol version
            case < ProtocolVersion.Min:
                throw new ArgumentException(
                    $"Protocol version '{config.ProtocolVersion}' too low. Must be in range: [{ProtocolVersion.Min}, {ProtocolVersion.Max}]",
                    nameof(config));
            case > ProtocolVersion.Max:
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

        // Ensure we have transport
        if (config.Transport == null)
        {
            throw new ArgumentException("Transport is required", nameof(config));
        }

        // Wrap transport if needed
        var transport = config.Transport as INodeAwareTransport ??
                        throw new ArgumentException("Transport must implement INodeAwareTransport", nameof(config));

        // Create memberlist
        var memberlist = new Memberlist(config, transport);

        // Initialize local node
        memberlist.InitializeLocalNode();

        //Update config with the actual bound port if it was 0
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
    public Node LocalNode => _localNode ?? throw new InvalidOperationException("Local node not initialized");

    /// <summary>
    /// Returns the number of members in the cluster from this node's perspective.
    /// </summary>
    public int NumMembers()
    {
        lock (NodeLock)
        {
            return NodeMap.Count;
        }
    }

    /// <summary>
    /// Returns the health score of the local node. Lower values are healthier.
    /// 0 is perfectly healthy.
    /// </summary>
    public int GetHealthScore()
    {
        return Awareness.GetHealthScore();
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
        return (int)Interlocked.CompareExchange(ref NumNodes, 0, 0);
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
    /// Gets the advertisement address and port.
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
        await _shutdownCts.CancelAsync();

        // Wait for background tasks to complete
        try
        {
            await Task.WhenAll(_backgroundTasks);
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
                            // Handle stream connection in the background
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
                                    await stream.DisposeAsync();
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
                        await Task.Delay(Config.GossipInterval, _shutdownCts.Token);
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
        if (Config.ProbeInterval > TimeSpan.Zero)
        {
            var probeTask = Task.Run(async () =>
            {
                try
                {
                    // Add initial random stagger to avoid synchronization
                    var stagger = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Config.ProbeInterval.TotalMilliseconds));
                    await Task.Delay(stagger, _shutdownCts.Token);

                    while (!_shutdownCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(Config.ProbeInterval, _shutdownCts.Token);
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

        // Push-pull scheduler - periodically performs full state sync with a random node
        if (Config.PushPullInterval > TimeSpan.Zero)
        {
            var pushPullTask = Task.Run(async () =>
            {
                try
                {
                    // Add initial random stagger to avoid synchronization
                    var stagger = TimeSpan.FromMilliseconds(Random.Shared.Next(0, (int)Config.PushPullInterval.TotalMilliseconds));
                    await Task.Delay(stagger, _shutdownCts.Token);

                    while (!_shutdownCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(Config.PushPullInterval, _shutdownCts.Token);
                            await PeriodicPushPullAsync();
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Push-pull scheduler error");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Push-pull scheduler crashed");
                }
            }, _shutdownCts.Token);
            _backgroundTasks.Add(pushPullTask);
        }
    }

    /// <summary>
    /// Performs periodic push-pull with a random node for full state synchronization.
    /// This ensures eventual consistency across the cluster.
    /// </summary>
    private async Task PeriodicPushPullAsync()
    {
        // Skip if we're leaving the cluster
        if (IsLeaving)
        {
            return;
        }

        // Get a random node to sync with
        NodeState? nodeState;
        lock (NodeLock)
        {
            if (Nodes.Count == 0)
            {
                return;
            }

            // Pick a random node
            var index = Random.Shared.Next(Nodes.Count);
            nodeState = Nodes[index];
        }

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

        var numCheck = 0;
        int maxChecks;

        lock (NodeLock)
        {
            maxChecks = Nodes.Count;
            if (maxChecks == 0)
            {
                return; // No nodes to probe
            }
        }

        while (numCheck < maxChecks)
        {
            NodeState? nodeToProbe;

            lock (NodeLock)
            {
                // Recheck the node count in case it changed
                if (Nodes.Count == 0)
                {
                    return; // No nodes to probe
                }

                // Handle wrap around
                if (_probeIndex >= Nodes.Count)
                {
                    _probeIndex = 0;
                }

                // Get candidate node
                var node = Nodes[_probeIndex];
                _probeIndex++;
                numCheck++;

                // Skip local node
                if (node.Name == Config.Name)
                {
                    continue;
                }

                // Skip dead or left nodes
                if (node.State is NodeStateType.Dead or NodeStateType.Left)
                {
                    continue;
                }

                nodeToProbe = node;
            }

            // Found a node to probe
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

        // Create a ping message
        var ping = new PingMessage
        {
            SeqNo = seqNo,
            Node = node.Name
        };

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
                Node = node.Name,  // Target node name for verification
                SourceAddr = advertiseAddr.GetAddressBytes(),
                SourcePort = (ushort)advertisePort,
                SourceNode = Config.Name
            };

            var pingBytes = Messages.MessageEncoder.Encode(MessageType.Ping, pingMsg);
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
                _logger?.LogWarning("Was able to connect to {Node} over TCP but UDP probes failed, network may be misconfigured", node.Name);

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
            if (IsLeaving)
            {
                return;
            }

            _logger?.LogWarning("Probe failed for node: {Node}, marking as suspect", node.Name);

            // Mark the node as suspect
            var suspect = new Messages.Suspect
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
            // Cleanup
            AckHandlers.TryRemove(seqNo, out _);
        }
    }

    /// <summary>
    /// Combines memberlist's internal broadcasts with delegate broadcasts.
    /// Mirrors Go's getBroadcasts() function that frames user messages.
    /// 
    /// Thread Safety: This method is thread-safe and can be called concurrently.
    /// - _broadcasts.GetBroadcasts() is thread-safe (TransmitLimitedQueue has internal locking)
    /// - _config.Delegate is read-only after initialization
    /// - Delegate.GetBroadcasts() must be thread-safe per IDelegate contract
    /// - All other operations use local variables (thread-local)
    /// 
    /// This matches the thread safety guarantees of Go's memberlist.getBroadcasts().
    /// </summary>
    /// <param name="overhead">Per-message overhead in bytes</param>
    /// <param name="limit">Total byte limit for all messages</param>
    /// <returns>List of broadcast messages, with user messages framed with MessageType.User byte</returns>
    private List<byte[]> GetBroadcasts(int overhead, int limit)
    {
        // Get memberlist messages first
        var toSend = Broadcasts.GetBroadcasts(overhead, limit);

        // Check if the user has a delegate with broadcasts
        var d = Config.Delegate;
        if (d == null) return toSend;
        // Determine the bytes used already
        var bytesUsed = toSend.Sum(msg => msg.Length + overhead);

        // Check space remaining for user messages
        var avail = limit - bytesUsed;
        const int userMsgOverhead = 1; // Frame overhead for user messages
        if (avail <= overhead + userMsgOverhead) return toSend;

        var userMsgs = d.GetBroadcasts(overhead + userMsgOverhead, avail);

        // Frame each user message with User type byte
        foreach (var msg in userMsgs)
        {
            var buf = new byte[1 + msg.Length];
            buf[0] = (byte)Messages.MessageType.User; // Frame with a User message type
            Array.Copy(msg, 0, buf, 1, msg.Length);
            toSend.Add(buf);
        }

        _logger?.LogDebug("[MEMBERLIST] Added {Count} user delegate broadcasts", userMsgs.Count);


        return toSend;
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
                    NodeStateType.Dead or NodeStateType.Left => (DateTimeOffset.UtcNow - node.StateChange) > Config.GossipToTheDeadTime,// Gossip to dead/left nodes if they transitioned recently                                                                                                                 // This allows leave messages to propagate even to nodes that already left
                    _ => true,// Exclude other states
                };
            });
        }

        if (kNodes.Count == 0)
        {
            return;
        }

        // Calculate bytes available for broadcasts
        var bytesAvail = Config.UDPBufferSize - Messages.MessageConstants.CompoundHeaderOverhead;

        _logger?.LogDebug("[GOSSIP] Starting gossip round to {Count} nodes, {Queued} broadcasts queued", kNodes.Count, Broadcasts.NumQueued());

        // CRITICAL: Get broadcasts ONCE per gossip interval, not per node!
        // The same messages are sent to all K random nodes in this interval
        var msgs = GetBroadcasts(Messages.MessageConstants.CompoundOverhead, bytesAvail);

        if (msgs.Count == 0)
        {
            _logger?.LogDebug("[GOSSIP] No broadcasts available, skipping gossip round");
            return;
        }

        foreach (var node in kNodes)
        {
            _logger?.LogInformation("[GOSSIP] *** Sending {Count} broadcasts to {Node} ***", msgs.Count, node.Name);

            var addr = new Transport.Address
            {
                Addr = $"{node.Addr}:{node.Port}",
                Name = node.Name
            };

            try
            {
                // Send a single message as-is
                var packet = msgs.Count == 1 ? msgs[0] : CompoundMessage.MakeCompoundMessage(msgs);

                // Encrypt if enabled (BEFORE adding label header, matching SendPacketAsync)
                if (Config.EncryptionEnabled() && Config.GossipVerifyOutgoing)
                {
                    try
                    {
                        var authData = System.Text.Encoding.UTF8.GetBytes(Config.Label ?? "");
                        var primaryKey = Config.Keyring?.GetPrimaryKey() ?? throw new InvalidOperationException("No primary key available");
                        packet = Security.EncryptPayload(1, primaryKey, packet, authData);
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

                _logger?.LogDebug("[GOSSIP] Successfully sent {Count} messages to {Node} at {Addr}", msgs.Count, node.Name, addr.Addr);
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
            var (streamLabel, peekedData) = await LabelHandler.RemoveLabelHeaderFromStreamAsync(stream, _shutdownCts.Token);

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

            // Read 4-byte length prefix for TCP framing (always present)
            var lengthBytes = new byte[4];
            lengthBytes[0] = buffer[0]; // First byte already read
            int totalRead = 1;
            while (totalRead < 4)
            {
                var bytesRead = await stream.ReadAsync(lengthBytes.AsMemory(totalRead, 4 - totalRead), _shutdownCts.Token);
                if (bytesRead == 0)
                {
                    throw new IOException("Connection closed while reading message length prefix");
                }
                totalRead += bytesRead;
            }

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }
            var messageLength = BitConverter.ToInt32(lengthBytes, 0);
            _logger?.LogDebug("Reading {Length} bytes of message data", messageLength);

            // Read exact message payload based on length prefix
            var messageData = new byte[messageLength];
            totalRead = 0;
            while (totalRead < messageLength)
            {
                var bytesRead = await stream.ReadAsync(messageData.AsMemory(totalRead, messageLength - totalRead), _shutdownCts.Token);
                if (bytesRead == 0)
                {
                    throw new IOException($"Connection closed while reading message payload ({totalRead}/{messageLength} bytes read)");
                }
                totalRead += bytesRead;
            }

            // Check if encryption is enabled - decrypt if needed
            if (Config.EncryptionEnabled())
            {
                _logger?.LogDebug("Decrypting incoming message (encryption enabled in config)");

                try
                {
                    var authData = System.Text.Encoding.UTF8.GetBytes(streamLabel);
                    messageData = Security.DecryptPayload([.. Config.Keyring!.GetKeys()], messageData, authData);
                }
                catch (Exception ex)
                {
                    if (Config.GossipVerifyIncoming)
                    {
                        _logger?.LogError(ex, "Failed to decrypt stream");
                        return;
                    }
                    else
                    {
                        _logger?.LogDebug(ex, "Failed to decrypt stream, treating as plaintext");
                    }
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

                            // Create a memory stream from decompressed payload (skip message type byte)
                            using var decompressedStream = new MemoryStream(decompressedPayload, 1, decompressedPayload.Length - 1);
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
            var header = await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.UserMsgHeader>(payloadStream, cancellationToken: _shutdownCts.Token);

            _logger?.LogDebug("User message header: {Length} bytes", header.UserMsgLen);

            // Read the user message payload (raw bytes, not MessagePack)
            if (header.UserMsgLen > 0)
            {
                var userMsgBytes = new byte[header.UserMsgLen];
                var totalRead = 0;

                while (totalRead < header.UserMsgLen)
                {
                    var bytesRead = await payloadStream.ReadAsync(userMsgBytes.AsMemory(totalRead, header.UserMsgLen - totalRead), _shutdownCts.Token);
                    if (bytesRead == 0)
                    {
                        throw new IOException("Connection closed while reading user message");
                    }
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
            if (userState != null && userState.Length > 0 && Config.Delegate != null)
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
    internal async Task SendPacketAsync(byte[] buffer, Transport.Address addr, CancellationToken cancellationToken = default)
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
                var primaryKey = Config.Keyring.GetPrimaryKey() ?? throw new InvalidOperationException("No primary key available");
                buffer = Security.EncryptPayload(1, primaryKey, buffer, authData);
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
    /// Sends a UDP packet to an address, adding a label header if configured.
    /// Internal helper that calls SendPacketAsync.
    /// </summary>
    internal async Task SendUdpAsync(byte[] buffer, Address addr, CancellationToken cancellationToken = default)
    {
        await SendPacketAsync(buffer, addr, cancellationToken);
    }

    /// <summary>
    /// Initializes the local node with an address from transport.
    /// </summary>
    private void InitializeLocalNode()
    {
        // Get advertise address from transport
        var (ip, port) = _transport.FinalAdvertiseAddr(Config.AdvertiseAddr, Config.AdvertisePort);

        lock (_advertiseLock)
        {
            _advertiseAddr = ip;
            _advertisePort = (ushort)port;
        }

        // Create the local node
        _localNode = new Node
        {
            Name = Config.Name,
            Addr = ip,
            Port = (ushort)port,
            Meta = Config.Delegate?.NodeMeta(ushort.MaxValue) ?? [],
            State = NodeStateType.Alive,
            PMin = ProtocolVersion.Min,
            PMax = ProtocolVersion.Max,
            PCur = Config.ProtocolVersion,
            DMin = Config.DelegateProtocolMin,
            DMax = Config.DelegateProtocolMax,
            DCur = Config.DelegateProtocolVersion
        };

        // Add ourselves to the node map
        var localState = new NodeState
        {
            Node = _localNode,
            State = NodeStateType.Alive,
            StateChange = DateTimeOffset.UtcNow,
            Incarnation = 0
        };

        NodeMap[Config.Name] = localState;

        lock (NodeLock)
        {
            Nodes.Add(localState);
        }
    }

    /// <summary>
    /// Sends a raw message over a stream with proper TCP framing.
    /// Format: [4-byte length prefix][message data (possibly compressed/encrypted)]
    /// </summary>
    private async Task RawSendMsgStreamAsync(NetworkStream conn, byte[] buf, string streamLabel, CancellationToken cancellationToken = default)
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
                var primaryKey = Config.Keyring.GetPrimaryKey() ?? throw new InvalidOperationException("No primary key available");
                buf = Security.EncryptPayload(1, primaryKey, buf, authData);
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
    internal async Task<bool> SendPingAndWaitForAckAsync(Address addr, Messages.PingMessage ping, DateTimeOffset deadline, CancellationToken cancellationToken = default)
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
            if (conn is not null)
            {
                await conn.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Encodes a message and queues it for broadcast to the cluster.
    /// </summary>
    internal void EncodeAndBroadcast(string node, MessageType msgType, object message)
    {
        EncodeBroadcastNotify(node, msgType, message, null);
    }

    /// <summary>
    /// Encodes a message and queues it for broadcast with notification when complete.
    /// </summary>
    private void EncodeBroadcastNotify(string node, MessageType msgType, object message, BroadcastNotifyChannel? notify)
    {
        try
        {
            // Convert protocol structures to MessagePack messages
            var msgToEncode = message switch
            {
                Alive alive => new AliveMessage
                {
                    Incarnation = alive.Incarnation,
                    Node = alive.Node ?? string.Empty,
                    Addr = alive.Addr ?? [],
                    Port = alive.Port,
                    Meta = alive.Meta ?? [],
                    Vsn = alive.Vsn ?? [],
                },
                Suspect suspect => new SuspectMessage
                {
                    Incarnation = suspect.Incarnation, Node = suspect.Node, From = suspect.From
                },
                Dead dead => new DeadMessage
                {
                    Incarnation = dead.Incarnation, Node = dead.Node, From = dead.From
                },
                _ => message
            };

            var encoded = Messages.MessageEncoder.Encode(msgType, msgToEncode);

            // Check size limits
            if (encoded.Length > Config.UDPBufferSize)
            {
                _logger?.LogError("Encoded {Type} message for {Node} is too large ({Size} > {Max})",
                    msgType, node, encoded.Length, Config.UDPBufferSize);
                return;
            }

            // Check if we should skip broadcasting our own messages based on config
            // EXCEPTION: Always broadcast Dead messages for graceful leave, even if reclaim time is 0
            if (node == Config.Name && Config.DeadNodeReclaimTime == TimeSpan.Zero && msgType != MessageType.Dead)
            {
                // Don't broadcast our own state changes if reclaim time is 0
                _logger?.LogDebug("Skipping broadcast for local node {Node} (reclaim time is 0)", node);
                return;
            }

            QueueBroadcast(node, msgType, encoded, notify);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to encode and broadcast {Type} for {Node}", msgType, node);
        }
    }

    /// <summary>
    /// Returns a list of all known live nodes.
    /// The node structures returned must not be modified.
    /// </summary>
    public List<Node> Members()
    {
        lock (NodeLock)
        {
            return [.. Nodes
                .Where(n => n.State != NodeStateType.Dead && n.State != NodeStateType.Left)
                .Select(n => n.Node)];
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
        {
            // Already left
            return null;
        }

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
    /// UpdateNode triggers re-advertising the local node's metadata to the cluster.
    /// This is useful when node metadata changes and needs to be propagated without
    /// restarting or rejoining the cluster. The method increments the incarnation
    /// number and broadcasts an alive message with the updated information.
    /// </summary>
    /// <param name="timeout">How long to wait for broadcast confirmation</param>
    /// <returns>Task that completes when an update is broadcast</returns>
    /// <exception cref="InvalidOperationException">Thrown if metadata exceeds the size limit</exception>
    public async Task UpdateNodeAsync(TimeSpan timeout)
    {
        // Step 1: Get metadata from a delegate
        var meta = Array.Empty<byte>();
        if (Config.Delegate != null)
        {
            meta = Config.Delegate.NodeMeta(MessageConstants.MetaMaxSize);

            // Step 2: Validate metadata size
            if (meta.Length > MessageConstants.MetaMaxSize)
            {
                throw new InvalidOperationException(
                    $"Node metadata exceeds maximum size of {MessageConstants.MetaMaxSize} bytes");
            }
        }

        // Step 3: Increment incarnation
        var inc = NextIncarnation();

        // Step 4: Create an alive message
        var localNode = LocalNode;
        var alive = new Messages.Alive
        {
            Incarnation = inc,
            Node = Config.Name,
            Addr = localNode.Addr.GetAddressBytes(),
            Port = localNode.Port,
            Meta = meta,
            Vsn =
            [
                Config.ProtocolVersion,
                Config.DelegateProtocolMin,
                Config.DelegateProtocolMax,
                Config.DelegateProtocolVersion
            ]
        };

        // Step 5: Update our own NodeState directly (don't go through HandleAliveNode for local updates)
        // HandleAliveNode would treat this as a refutation scenario and increment incarnation again
        if (NodeMap.TryGetValue(Config.Name, out var localState))
        {
            localState.Incarnation = inc;
            localState.Node.Meta = meta;

            // Update the cached local node reference
            _localNode = new Node
            {
                Name = localState.Node.Name,
                Addr = localState.Node.Addr,
                Port = localState.Node.Port,
                Meta = meta,
                State = localState.Node.State,
                PMin = localState.Node.PMin,
                PMax = localState.Node.PMax,
                PCur = localState.Node.PCur,
                DMin = localState.Node.DMin,
                DMax = localState.Node.DMax,
                DCur = localState.Node.DCur
            };
        }

        // Step 5b: Notify event delegate about local update (so Serf sees the new tags)
        if (Config.Events != null && localState != null)
        {
            Config.Events.NotifyUpdate(localState.Node);
        }

        // Step 5c: Broadcast the update to the cluster
        EncodeAndBroadcast(Config.Name, Messages.MessageType.Alive, alive);

        // Step 6: Wait briefly to allow broadcast to be queued
        // In Go, this waits for the broadcast to be transmitted, but in our implementation
        // EncodeAndBroadcast queues the message immediately, so we just need a small delay
        // to ensure the broadcast queue has processed it
        bool hasOtherNodes;
        lock (NodeLock)
        {
            // anyAlive() equivalent: Check for nodes that are not dead/left and not ourselves
            hasOtherNodes = Nodes.Any(n =>
                n.State != NodeStateType.Dead &&
                n.State != NodeStateType.Left &&
                n.Name != Config.Name);
        }

        if (hasOtherNodes && timeout > TimeSpan.Zero)
        {
            // Brief delay to allow broadcast queue to process
            // This matches the Go behavior where we wait for the broadcast to be sent
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    /// <summary>
    /// Join is used to take an existing Memberlist and attempt to join a cluster
    /// by contacting all the given hosts and performing a state sync.
    /// Returns the number of nodes successfully contacted and an error if none could be reached.
    /// </summary>
    public async Task<(int NumJoined, Exception? Error)> JoinAsync(IEnumerable<string> existing, CancellationToken cancellationToken = default)
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
        if (userState != null && userState.Length > 0 && Config.Delegate != null)
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
    internal async Task<(List<Messages.PushNodeState> RemoteNodes, byte[]? UserState)> SendAndReceiveStateAsync(
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
                var read = await conn.ReadAsync(messageData.AsMemory(totalRead, messageLength - totalRead), cancellationToken);
                if (read == 0)
                {
                    throw new IOException($"Connection closed while reading response ({totalRead}/{messageLength} bytes read)");
                }
                totalRead += read;
            }

            // Decrypt if encryption is enabled
            if (Config.EncryptionEnabled())
            {
                _logger?.LogDebug("Decrypting push-pull response");
                var authData = System.Text.Encoding.UTF8.GetBytes(Config.Label);
                messageData = Security.DecryptPayload([.. Config.Keyring!.GetKeys()], messageData, authData);
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
                    var errResp = await MessagePack.MessagePackSerializer.DeserializeAsync<Messages.ErrRespMessage>(
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
    private async Task SendLocalStateAsync(NetworkStream conn, bool join, string? streamLabel, CancellationToken cancellationToken)
    {
        conn.Socket.SendTimeout = (int)Config.TCPTimeout.TotalMilliseconds;

        // Prepare a local node state - include ALL states (Alive, Suspect, Dead, Left)
        // This matches Go memberlist behavior and is critical for rejoined scenarios.
        // Dead/Left nodes MUST be included, so rejoining nodes can see their own tombstone
        // and trigger refutation by incrementing incarnation.
        List<Messages.PushNodeState> localNodes;
        lock (NodeLock)
        {
            localNodes = [.. Nodes
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
            await MessagePack.MessagePackSerializer.SerializeAsync(payloadMs, node, cancellationToken: cancellationToken);
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
        return read != header.UserStateLen ? 
            throw new IOException($"Expected {header.UserStateLen} bytes of user state but got {read}") : 
            (remoteNodes, userState);
    }

    /// <summary>
    /// Queues a broadcast for dissemination to the cluster.
    /// </summary>
    private void QueueBroadcast(string node, MessageType msgType, byte[] message, BroadcastNotifyChannel? notify)
    {
        // Create a broadcast with notification channel - it will call notify.Notify() when Finished() is invoked
        // by the TransmitLimitedQueue (either when invalidated or transmit limit reached)
        IBroadcast broadcast = msgType switch
        {
            MessageType.Alive => new AliveMessageBroadcast(node, message, notify),
            MessageType.Suspect => new SuspectMessageBroadcast(node, message, notify),
            MessageType.Dead => new DeadMessageBroadcast(node, message, notify),
            _ => throw new ArgumentException($"Unsupported message type for broadcast: {msgType}", nameof(msgType))
        };

        Broadcasts.QueueBroadcast(broadcast);
        _logger?.LogDebug("[BROADCAST] Queued {Type} broadcast for {Node}, queue size: {Size}", msgType, node, Broadcasts.NumQueued());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Try async dispose first, but if we must use sync Dispose,
        // use Task.Run to avoid deadlocks in synchronization contexts
        try
        {
            Task.Run(ShutdownAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during synchronous disposal");
        }
        _shutdownCts.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await ShutdownAsync();
            _shutdownCts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
