// Ported from: github.com/hashicorp/memberlist/memberlist.go
// Copyright (c) Boolhak, Inc.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using NSerf.Memberlist.Broadcast;
using NSerf.Memberlist.Configuration;
using NSerf.Memberlist.Exceptions;
using NSerf.Memberlist.Handlers;
using NSerf.Memberlist.Messages;
using NSerf.Memberlist.Security;
using NSerf.Memberlist.State;
using NSerf.Memberlist.Transport;

namespace NSerf.Memberlist;

/// <summary>
/// Memberlist manages cluster membership and member failure detection using a gossip-based protocol.
/// It is eventually consistent but converges quickly. Node failures are detected, and network partitions
/// are partially tolerated by attempting to communicate with potentially dead nodes through multiple routes.
/// </summary>
public partial class Memberlist : IDisposable, IAsyncDisposable
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
        var transport = VerifyCanCreate(config);
        var memberlist = new Memberlist(config, transport);
        memberlist.InitializeLocalNode();
        BindPort(config, transport);
        memberlist.StartBackgroundListeners();
        return memberlist;
    }

    private static void BindPort(MemberlistConfig config, INodeAwareTransport transport)
    {
        if (config.BindPort == 0 && transport is NetTransport netTransport)
            config.BindPort = netTransport.GetAutoBindPort();
    }

    private static INodeAwareTransport VerifyCanCreate(MemberlistConfig config)
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

        if (config.Transport == null)
        {
            throw new ArgumentException("Transport is required", nameof(config));
        }

        // Wrap transport if needed
        var transport = config.Transport as INodeAwareTransport ??
                        throw new ArgumentException("Transport must implement INodeAwareTransport", nameof(config));
        return transport;
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
    public uint PushPullRequests => Interlocked.CompareExchange(ref _pushPullReq, 0, 0);

    /// <summary>
    /// Checks if we're in the leave state.
    /// </summary>
    public bool IsLeaving => Interlocked.CompareExchange(ref _leave, 0, 0) == 1;

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
                    Incarnation = suspect.Incarnation,
                    Node = suspect.Node,
                    From = suspect.From
                },
                Dead dead => new DeadMessage
                {
                    Incarnation = dead.Incarnation,
                    Node = dead.Node,
                    From = dead.From
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
        var alive = new Alive
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
        EncodeAndBroadcast(Config.Name, MessageType.Alive, alive);

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
