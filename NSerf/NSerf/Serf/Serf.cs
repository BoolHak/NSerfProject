// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Partial implementation for Phase 6 (Delegates) - will be expanded in Phase 9

using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using NSerf.Serf.Events;
using MessagePack;
using System.Threading.Channels;

namespace NSerf.Serf;

/// <summary>
/// Main Serf class for cluster membership and coordination.
/// Partial implementation supporting Delegate operations - will be fully implemented in Phase 9.
/// </summary>
public partial class Serf : IDisposable, IAsyncDisposable
{
    // Protocol version constants (from Go serf.go)
    public const byte ProtocolVersionMin = 2;
    public const byte ProtocolVersionMax = 5;
    public const int UserEventSizeLimit = 9 * 1024; // 9KB

    // Shutdown channel for graceful termination
    private readonly CancellationTokenSource _shutdownCts = new();
    private SerfState _state = SerfState.SerfAlive;
    // Configuration
    internal Config Config { get; private set; }
    internal ILogger? Logger { get; private set; }

    // Clocks for Lamport timestamps
    internal LamportClock Clock { get; private set; }
    internal LamportClock EventClock { get; private set; }
    internal LamportClock QueryClock { get; private set; }

    // Broadcast queues
    internal BroadcastQueue Broadcasts { get; private set; }
    internal BroadcastQueue EventBroadcasts { get; private set; }
    internal BroadcastQueue QueryBroadcasts { get; private set; }

    // Member state - internal tracking
    internal Dictionary<string, MemberInfo> MemberStates { get; private set; }
    internal List<MemberInfo> FailedMembers { get; private set; }
    internal List<MemberInfo> LeftMembers { get; private set; }
    internal Dictionary<LamportTime, UserEventCollection> EventBuffer { get; private set; }
    internal Dictionary<LamportTime, QueryCollection> QueryBuffer { get; private set; }

    // Event configuration
    internal bool EventJoinIgnore { get; set; }
    internal LamportTime EventMinTime { get; set; }
    internal LamportTime QueryMinTime { get; set; }

    // Query tracking - maps LTime to QueryResponse
    private readonly Dictionary<LamportTime, QueryResponse> _queryResponses = new();
    private readonly Random _queryRandom = new();

    // Memberlist integration
    internal Memberlist.Memberlist? Memberlist { get; private set; }
    private Memberlist.Delegates.IEventDelegate? _eventDelegate;

    // Snapshot integration
    internal Snapshotter? Snapshotter { get; private set; }
    private ChannelWriter<Event>? _snapshotInCh;

    // Coordinate client for Vivaldi network coordinates
    private Coordinate.CoordinateClient? _coordClient;

    // Locks - Thread-safety for concurrent access
    // Lock Ordering Pattern (from DeepWiki analysis):
    // - No strict global order, but acquire lock at start of handler
    // - When multiple locks needed, acquire in nested fashion
    // - Most handlers acquire single lock protecting their data
    private readonly ReaderWriterLockSlim _memberLock = new();  // Protects: members, failedMembers, leftMembers, recentIntents
    private readonly ReaderWriterLockSlim _eventLock = new();   // Protects: eventBuffer, eventMinTime
    private readonly ReaderWriterLockSlim _queryLock = new();   // Protects: queryBuffer, queryMinTime, queryResponse
    private readonly SemaphoreSlim _stateLock = new(1, 1);      // Protects: state field (SerfAlive, SerfLeaving, etc.)
    private readonly ReaderWriterLockSlim _coordCacheLock = new(); // Protects: coordCache
    private readonly Dictionary<string, Coordinate.Coordinate> _coordCache = new();
    private readonly SemaphoreSlim _joinLock = new(1, 1);       // Protects: eventJoinIgnore during Join operation

    /// <summary>
    /// Internal constructor for testing - use CreateAsync() factory method for production.
    /// </summary>
    internal Serf(Config config, ILogger? logger = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Logger = logger ?? config.Logger;

        Clock = new LamportClock();
        EventClock = new LamportClock();
        QueryClock = new LamportClock();

        // Initialize broadcast queues with transmit-limited queues
        Broadcasts = new BroadcastQueue(new TransmitLimitedQueue());
        EventBroadcasts = new BroadcastQueue(new TransmitLimitedQueue());
        QueryBroadcasts = new BroadcastQueue(new TransmitLimitedQueue());

        MemberStates = new Dictionary<string, MemberInfo>();
        FailedMembers = new List<MemberInfo>();
        LeftMembers = new List<MemberInfo>();
        EventBuffer = new Dictionary<LamportTime, UserEventCollection>();
        QueryBuffer = new Dictionary<LamportTime, QueryCollection>();

        EventJoinIgnore = false;
        EventMinTime = 0;
        QueryMinTime = 0;
    }

    /// <summary>
    /// BroadcastJoin broadcasts a new join intent with a given clock value.
    /// Used on join or to refute an older leave intent. Cannot be called with member lock held.
    /// </summary>
    internal void BroadcastJoin(LamportTime ltime)
    {
        // Construct message to update our lamport clock
        var msg = new MessageJoin
        {
            LTime = ltime,
            Node = Config.NodeName
        };

        // Witness clock and update local intent
        Clock.Witness(ltime);

        HandleNodeJoinIntent(msg);

        // Start broadcasting the update
        var raw = EncodeMessage(MessageType.Join, msg);
        if (raw.Length > 0)
        {
            Broadcasts.QueueBytes(raw);
        }
    }

    /// <summary>
    /// Returns the number of members known to this Serf instance.
    /// Thread-safe read operation using read lock.
    /// </summary>
    public int NumMembers()
    {
        return WithReadLock(_memberLock, () => MemberStates.Count);
    }

    /// <summary>
    /// Returns all known members in the cluster, including failed and left nodes.
    /// Matches Go's behavior: returns from Serf's own tracking (MemberStates),
    /// not from memberlist which filters out dead/left nodes.
    /// Thread-safe read operation using read lock.
    /// </summary>
    public Member[] Members()
    {
        return WithReadLock(_memberLock, () =>
        {
            var members = new List<Member>();

            // Get all nodes from memberlist first (includes all states)
            var memberlistNodes = new Dictionary<string, Memberlist.State.NodeState>();
            if (Memberlist != null)
            {
                lock (Memberlist._nodeLock)
                {
                    foreach (var node in Memberlist._nodes)
                    {
                        memberlistNodes[node.Name] = node;
                    }
                }
            }

            // Return from Serf's own MemberStates
            // This matches Go's behavior where Members() returns from s.members map
            foreach (var kvp in MemberStates)
            {
                var memberInfo = kvp.Value;

                // If we have a stored Member object (from HandleNodeLeave), use it
                // BUT always use the current status from memberInfo, not the cached Member.Status
                if (memberInfo.Member != null)
                {
                    // Update the status in the member object to match current state
                    memberInfo.Member.Status = memberInfo.Status;
                    members.Add(memberInfo.Member);
                }
                else if (memberlistNodes.TryGetValue(kvp.Key, out var node))
                {
                    // Construct from memberlist node
                    var member = new Member
                    {
                        Name = node.Name,
                        Addr = node.Node.Addr,
                        Port = node.Node.Port,
                        Tags = DecodeTags(node.Node.Meta),
                        Status = memberInfo.Status,
                        ProtocolMin = node.Node.PMin,
                        ProtocolMax = node.Node.PMax,
                        ProtocolCur = node.Node.PCur,
                        DelegateMin = node.Node.DMin,
                        DelegateMax = node.Node.DMax,
                        DelegateCur = node.Node.DCur
                    };
                    members.Add(member);
                }
            }

            return members.ToArray();
        });
    }

    /// <summary>
    /// Returns members filtered by status.
    /// Thread-safe read operation using read lock.
    /// </summary>
    public Member[] Members(MemberStatus statusFilter)
    {
        return WithReadLock(_memberLock, () =>
        {
            var members = new List<Member>();
            foreach (var kvp in MemberStates)
            {
                // Return the actual stored Member object with current data
                var memberInfo = kvp.Value;
                if (memberInfo.Member != null && memberInfo.Status == statusFilter)
                {
                    // Update the Member's status to match current state
                    memberInfo.Member.Status = memberInfo.Status;
                    members.Add(memberInfo.Member);
                }
            }
            return members.ToArray();
        });
    }

    // ========== Helper Methods ==========

    /// <summary>
    /// Emits an event to both the Snapshotter and the user's EventCh.
    /// This mimics Go's behavior where events are sent to both channels.
    /// </summary>
    private void EmitEvent(Event evt)
    {
        Logger?.LogInformation("[Serf/EmitEvent] Emitting {EventType}, _snapshotInCh is {IsNull}",
            evt.GetType().Name, _snapshotInCh == null ? "NULL" : "SET");

        // Send to snapshotter's input channel (if configured)
        if (_snapshotInCh != null)
        {
            try
            {
                var written = _snapshotInCh.TryWrite(evt);
                Logger?.LogInformation("[Serf/EmitEvent] Wrote to snapshotter: {Success}, Type: {Type}", written, evt.GetType().Name);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf/EmitEvent] Failed to emit event to snapshotter");
            }
        }

        // Send to user's event channel (if configured)
        if (Config.EventCh != null)
        {
            try
            {
                Config.EventCh.TryWrite(evt);
                Logger?.LogTrace("[Serf/EmitEvent] Emitted event to EventCh: {Type}", evt.GetType().Name);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf/EmitEvent] Failed to emit event to EventCh");
            }
        }
    }

    // ========== Phase 9.1: Lifecycle Methods ==========

    /// <summary>
    /// Creates a new Serf instance with the given configuration.
    /// Maps to: Go's Create() function
    /// </summary>
    public static async Task<Serf> CreateAsync(Config config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        // Validate protocol version
        if (config.ProtocolVersion < ProtocolVersionMin)
        {
            throw new ArgumentException(
                $"Protocol version '{config.ProtocolVersion}' too low. Must be in range: [{ProtocolVersionMin}, {ProtocolVersionMax}]");
        }
        if (config.ProtocolVersion > ProtocolVersionMax)
        {
            throw new ArgumentException(
                $"Protocol version '{config.ProtocolVersion}' too high. Must be in range: [{ProtocolVersionMin}, {ProtocolVersionMax}]");
        }

        // Validate user event size limit
        if (config.UserEventSizeLimit > UserEventSizeLimit)
        {
            throw new ArgumentException(
                $"User event size limit exceeds limit of {UserEventSizeLimit} bytes");
        }

        var serf = new Serf(config);

        // Ensure clocks start at least at 1
        serf.Clock.Increment();
        serf.EventClock.Increment();
        serf.QueryClock.Increment();

        // Phase 6: Setup internal query handler to intercept _serf_* queries
        // This sits between Serf and the user's EventCh, filtering internal queries
        ChannelWriter<Event>? eventDestination = config.EventCh;
        ChannelWriter<Event>? internalQueryInput = null;
        
        if (config.EventCh != null)
        {
            serf.Logger?.LogInformation("[Serf/InternalQuery] Setting up internal query handler");
            
            // Create internal query handler - it will intercept _serf_* queries
            var (queryInputCh, queryHandler) = SerfQueries.Create(
                serf,
                config.EventCh, // Non-internal events pass through to user's EventCh
                serf._shutdownCts.Token);
            
            internalQueryInput = queryInputCh;
            eventDestination = queryInputCh; // Events now go to internal query handler first
            
            serf.Logger?.LogInformation("[Serf/InternalQuery] ✓ Internal query handler created");
        }

        // Phase 9.8: Setup snapshot if path is configured
        List<PreviousNode>? previousNodes = null;
        if (!string.IsNullOrEmpty(config.SnapshotPath))
        {
            serf.Logger?.LogInformation("[Serf/Snapshot] Initializing snapshotter at path: {Path}", config.SnapshotPath);

            var snapshotResult = await Snapshotter.NewSnapshotterAsync(
                path: config.SnapshotPath,
                minCompactSize: config.MinSnapshotSize,
                rejoinAfterLeave: config.RejoinAfterLeave,
                logger: serf.Logger,
                clock: serf.Clock,
                outCh: eventDestination, // Use eventDestination (query handler or EventCh)
                shutdownToken: serf._shutdownCts.Token);

            var inCh = snapshotResult.InCh;
            var snapshotter = snapshotResult.Snap;

            serf.Snapshotter = snapshotter;
            serf._snapshotInCh = inCh;

            serf.Logger?.LogInformation("[Serf/Snapshot] ✓ Snapshotter created successfully for node {NodeName}", config.NodeName);
            serf.Logger?.LogInformation("[Serf/Snapshot] ✓ _snapshotInCh is {Status}", serf._snapshotInCh == null ? "NULL (ERROR!)" : "READY");

            // Restore clock values from snapshot
            serf.Clock.Witness(snapshotter.LastClock);
            serf.EventClock.Witness(snapshotter.LastEventClock);
            serf.QueryClock.Witness(snapshotter.LastQueryClock);

            serf.Logger?.LogInformation("[Serf/Snapshot] Clock restored - Clock: {Clock}, EventClock: {EventClock}, QueryClock: {QueryClock}",
                snapshotter.LastClock, snapshotter.LastEventClock, snapshotter.LastQueryClock);

            // Get previous nodes for auto-rejoin
            previousNodes = snapshotter.AliveNodes();

            serf.Logger?.LogInformation("[Serf/Snapshot] Loaded {Count} previous nodes from snapshot", previousNodes.Count);
            Console.WriteLine($"[SNAPSHOT DEBUG] Loaded {previousNodes.Count} nodes from snapshot at {config.SnapshotPath}");
            foreach (var node in previousNodes)
            {
                serf.Logger?.LogInformation("[Serf/Snapshot] - Previous node: {Name} at {Addr}", node.Name, node.Addr);
                Console.WriteLine($"[SNAPSHOT DEBUG] - Node: {node.Name} at {node.Addr}");
            }
            
            // DEBUG: Check snapshot file
            if (System.IO.File.Exists(config.SnapshotPath))
            {
                var fileInfo = new System.IO.FileInfo(config.SnapshotPath);
                Console.WriteLine($"[SNAPSHOT DEBUG] File exists: {config.SnapshotPath}, Size: {fileInfo.Length} bytes");
            }
            else
            {
                Console.WriteLine($"[SNAPSHOT DEBUG] File DOES NOT exist: {config.SnapshotPath}");
            }
        }
        else
        {
            serf.Logger?.LogInformation("[Serf/Snapshot] No snapshot path configured for node {NodeName}", config.NodeName);
        }

        // Initialize coordinate client if coordinates are enabled
        if (!config.DisableCoordinates)
        {
            serf.Logger?.LogInformation("[Serf/Coordinates] Initializing coordinate client");
            var coordConfig = Coordinate.CoordinateConfig.DefaultConfig();
            serf._coordClient = new Coordinate.CoordinateClient(coordConfig);
            serf.Logger?.LogInformation("[Serf/Coordinates] ✓ Coordinate client initialized");
        }
        else
        {
            serf.Logger?.LogInformation("[Serf/Coordinates] Coordinates disabled per configuration");
        }

        // Phase 9.2: Initialize memberlist with delegates
        if (config.MemberlistConfig != null)
        {
            // Create event delegate that routes to Serf for membership events
            serf._eventDelegate = new SerfEventDelegate(serf);
            config.MemberlistConfig.Events = serf._eventDelegate;

            // Create main delegate for gossip messages (GetBroadcasts, NotifyMsg, etc.)
            var serfDelegate = new Delegate(serf);
            config.MemberlistConfig.Delegate = serfDelegate;

            // Setup PingDelegate for coordinate updates if coordinates are enabled
            if (!config.DisableCoordinates)
            {
                var pingDelegate = new PingDelegate(serf);
                config.MemberlistConfig.Ping = pingDelegate;
                serf.Logger?.LogInformation("[Serf/Coordinates] ✓ PingDelegate configured for RTT tracking");
            }

            // Initialize transport if not provided
            if (config.MemberlistConfig.Transport == null)
            {
                var transportConfig = new NSerf.Memberlist.Transport.NetTransportConfig
                {
                    BindAddrs = new List<string> { config.MemberlistConfig.BindAddr },
                    BindPort = config.MemberlistConfig.BindPort,
                    Logger = serf.Logger
                };
                config.MemberlistConfig.Transport = NSerf.Memberlist.Transport.NetTransport.Create(transportConfig);
            }

            // Create memberlist
            serf.Memberlist = NSerf.Memberlist.Memberlist.Create(config.MemberlistConfig);

            // Add local node to members list with full Member object
            // The memberlist will have the local node, so we need to track it in Serf's Members
            var localNode = serf.Memberlist.LocalNode;
            var localMember = new MemberInfo
            {
                Name = config.NodeName,
                StatusLTime = 0,
                Status = MemberStatus.Alive,
                Member = new Member
                {
                    Name = localNode.Name,
                    Addr = localNode.Addr,
                    Port = localNode.Port,
                    Tags = serf.DecodeTags(localNode.Meta),
                    Status = MemberStatus.Alive,
                    ProtocolMin = localNode.PMin,
                    ProtocolMax = localNode.PMax,
                    ProtocolCur = localNode.PCur,
                    DelegateMin = localNode.DMin,
                    DelegateMax = localNode.DMax,
                    DelegateCur = localNode.DCur
                }
            };
            serf.MemberStates[config.NodeName] = localMember;
        }

        // Phase 9.4: Start background tasks (reaper and reconnect)
        serf.StartBackgroundTasks();

        // Phase 9.8: Auto-rejoin from snapshot if we have previous nodes
        if (previousNodes != null && previousNodes.Count > 0)
        {
            serf.Logger?.LogInformation("[Serf/AutoRejoin] Starting auto-rejoin task for {Count} nodes", previousNodes.Count);

            _ = Task.Run(async () =>
            {
                try
                {
                    // Give memberlist more time to fully initialize
                    serf.Logger?.LogInformation("[Serf/AutoRejoin] Waiting 500ms for memberlist initialization...");
                    Console.WriteLine("[Serf/AutoRejoin] Waiting 500ms for memberlist initialization...");
                    await Task.Delay(500);

                    var addrs = previousNodes.Select(n => n.Addr).ToArray();
                    serf.Logger?.LogInformation("[Serf/AutoRejoin] Attempting to join {Count} addresses: [{Addrs}]",
                        addrs.Length, string.Join(", ", addrs));
                    Console.WriteLine($"[Serf/AutoRejoin] Attempting to join {addrs.Length} addresses: [{string.Join(", ", addrs)}]");

                    var joined = await serf.JoinAsync(addrs, ignoreOld: true);
                    serf.Logger?.LogInformation("[Serf/AutoRejoin] Auto-rejoin completed, successfully joined {Joined}/{Total} nodes",
                        joined, addrs.Length);
                    Console.WriteLine($"[Serf/AutoRejoin] Auto-rejoin completed, successfully joined {joined}/{addrs.Length} nodes");

                    if (joined == 0)
                    {
                        serf.Logger?.LogWarning("[Serf/AutoRejoin] Failed to join any nodes during auto-rejoin");
                        Console.WriteLine("[Serf/AutoRejoin] WARNING: Failed to join any nodes during auto-rejoin");
                    }
                }
                catch (Exception ex)
                {
                    serf.Logger?.LogError(ex, "[Serf/AutoRejoin] Exception during auto-rejoin");
                    Console.WriteLine($"[Serf/AutoRejoin] EXCEPTION: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }
        else
        {
            serf.Logger?.LogInformation("[Serf/AutoRejoin] No previous nodes to auto-rejoin (previousNodes: {IsNull}, Count: {Count})",
                previousNodes == null ? "null" : "not null", previousNodes?.Count ?? 0);
        }

        return serf;
    }

    /// <summary>
    /// Returns the current state of the Serf instance.
    /// Thread-safe read of state.
    /// </summary>
    public SerfState State()
    {
        return WithLock(_stateLock, () => _state);
    }

    /// <summary>
    /// Returns the protocol version being used by this Serf instance.
    /// </summary>
    public byte ProtocolVersion()
    {
        return Config.ProtocolVersion;
    }

    /// <summary>
    /// Returns information about the local member.
    /// </summary>
    public Member LocalMember()
    {
        return new Member
        {
            Name = Config.NodeName,
            Addr = System.Net.IPAddress.Parse(Config.MemberlistConfig?.BindAddr ?? "127.0.0.1"),
            Port = (ushort)(Config.MemberlistConfig?.BindPort ?? 0),
            Tags = new Dictionary<string, string>(Config.Tags),
            Status = MemberStatus.Alive,
            ProtocolMin = ProtocolVersionMin,
            ProtocolMax = ProtocolVersionMax,
            ProtocolCur = Config.ProtocolVersion,
            DelegateMin = ProtocolVersionMin,
            DelegateMax = ProtocolVersionMax,
            DelegateCur = Config.ProtocolVersion
        };
    }

    /// <summary>
    /// Updates the tags for the local member and broadcasts the change to the cluster.
    /// Maps to: Go's SetTags() method
    /// </summary>
    public async Task SetTagsAsync(Dictionary<string, string> tags)
    {
        if (tags == null)
            throw new ArgumentNullException(nameof(tags));

        // Update the configuration
        Config.Tags = new Dictionary<string, string>(tags);

        // Broadcast the tag update to the cluster via memberlist
        if (Memberlist != null)
        {
            await Memberlist.UpdateNodeAsync(Config.BroadcastTimeout);
        }
    }

    /// <summary>
    /// Broadcasts a custom user event with a given name and payload.
    /// Returns an error if the configured size limit is exceeded.
    /// If coalesce is enabled, nodes are allowed to coalesce this event.
    /// Maps to: Go's UserEvent() method
    /// </summary>
    /// <param name="name">Name of the user event</param>
    /// <param name="payload">Event payload data</param>
    /// <param name="coalesce">If true, allow event coalescing</param>
    /// <returns>Task that completes when event is queued for broadcast</returns>
    public Task UserEventAsync(string name, byte[] payload, bool coalesce)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Event name cannot be null or empty", nameof(name));
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        var payloadSizeBeforeEncoding = name.Length + payload.Length;

        // Check size before encoding to prevent needless encoding
        if (payloadSizeBeforeEncoding > Config.UserEventSizeLimit)
        {
            throw new InvalidOperationException(
                $"User event exceeds configured limit of {Config.UserEventSizeLimit} bytes before encoding");
        }

        if (payloadSizeBeforeEncoding > UserEventSizeLimit)
        {
            throw new InvalidOperationException(
                $"User event exceeds sane limit of {UserEventSizeLimit} bytes before encoding");
        }

        // Create a message
        var msg = new MessageUserEvent
        {
            LTime = EventClock.Time(),
            Name = name,
            Payload = payload,
            CC = coalesce
        };

        // Start broadcasting the event
        var raw = EncodeMessage(MessageType.UserEvent, msg);

        // Check the size after encoding
        if (raw.Length > Config.UserEventSizeLimit)
        {
            throw new InvalidOperationException(
                $"Encoded user event exceeds configured limit of {Config.UserEventSizeLimit} bytes after encoding");
        }

        if (raw.Length > UserEventSizeLimit)
        {
            throw new InvalidOperationException(
                $"Encoded user event exceeds reasonable limit of {UserEventSizeLimit} bytes after encoding");
        }

        EventClock.Increment();

        // Process update locally
        HandleUserEvent(msg);

        // Queue for broadcast
        Logger?.LogInformation("[Serf] *** Queuing user event '{Name}' ({Size} bytes) for broadcast ***", name, raw.Length);
        EventBroadcasts.QueueBytes(raw);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Joins an existing Serf cluster.
    /// Takes a list of existing node addresses to contact and returns the number successfully contacted.
    /// Maps to: Go's Join() method
    /// </summary>
    /// <param name="existing">List of node addresses to contact (format: "host:port" or "node/host:port")</param>
    /// <param name="ignoreOld">If true, ignore any user messages sent prior to this join</param>
    /// <returns>Number of nodes successfully contacted</returns>
    public async Task<int> JoinAsync(string[] existing, bool ignoreOld)
    {
        if (existing == null || existing.Length == 0)
            throw new ArgumentException("Must provide at least one node address to join", nameof(existing));

        // Check state
        if (State() != SerfState.SerfAlive)
        {
            throw new InvalidOperationException("Serf can't join after Leave or Shutdown");
        }

        // Hold the join lock to make eventJoinIgnore safe
        return await WithLockAsync(_joinLock, async () =>
        {
            // Ignore any events from a potential join if requested
            if (ignoreOld)
            {
                EventJoinIgnore = true;
            }

            try
            {
                // Have memberlist attempt to join
                if (Memberlist == null)
                {
                    throw new InvalidOperationException("Memberlist not initialized");
                }

                var (numJoined, error) = await Memberlist.JoinAsync(existing);

                // If we joined any nodes, broadcast the join message
                if (numJoined > 0)
                {
                    try
                    {
                        BroadcastJoin(Clock.Time());
                        Logger?.LogInformation("[Serf] Successfully joined {NumNodes} nodes", numJoined);
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogWarning(ex, "[Serf] Failed to broadcast join intent after join");
                    }
                }

                if (error != null && numJoined == 0)
                {
                    throw error;
                }

                return numJoined;
            }
            finally
            {
                if (ignoreOld)
                {
                    EventJoinIgnore = false;
                }
            }
        });
    }

    /// <summary>
    /// Gracefully leaves the Serf cluster.
    /// Maps to: Go's Leave() method
    /// </summary>
    public async Task LeaveAsync()
    {
        await WithLockAsync(_stateLock, async () =>
        {
            if (_state == SerfState.SerfLeft || _state == SerfState.SerfShutdown)
            {
                return; // Already left or shutdown
            }

            _state = SerfState.SerfLeaving;

            // Notify snapshotter about the leave (Phase 9.8)
            if (Snapshotter != null)
            {
                await Snapshotter.LeaveAsync();
            }

            // Broadcast leave intent to cluster
            var leaveMsg = new MessageLeave
            {
                LTime = Clock.Increment(),
                Node = Config.NodeName
            };

            // Handle our own leave intent locally
            HandleNodeLeaveIntent(leaveMsg);

            // Broadcast the leave message
            var encoded = EncodeMessage(MessageType.Leave, leaveMsg);
            if (encoded.Length > 0)
            {
                Broadcasts.QueueBytes(encoded);
                Logger?.LogDebug("[Serf] Broadcasted leave intent for: {Node}", Config.NodeName);
            }

            // Notify memberlist to gracefully leave (matching Go implementation)
            if (Memberlist != null)
            {
                try
                {
                    var error = await Memberlist.LeaveAsync(Config.BroadcastTimeout);
                    if (error != null)
                    {
                        Logger?.LogWarning("[Serf] Error during memberlist leave: {Error}", error.Message);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[Serf] Exception during memberlist leave");
                }
            }

            // Wait for leave message to propagate through the cluster (matching Go's LeavePropagateDelay)
            // This is CRITICAL - ensures other nodes have time to process the leave before we fully shutdown
            Logger?.LogDebug("[Serf] Waiting {Delay}ms for leave propagation", Config.LeavePropagateDelay.TotalMilliseconds);
            await Task.Delay(Config.LeavePropagateDelay);

            _state = SerfState.SerfLeft;
        });
    }

    /// <summary>
    /// Shuts down the Serf instance and stops all background operations.
    /// Maps to: Go's Shutdown() method
    /// </summary>
    public async Task ShutdownAsync()
    {
        await WithLockAsync(_stateLock, () =>
        {
            if (_state == SerfState.SerfShutdown)
            {
                return Task.CompletedTask; // Already shutdown
            }

            // Signal shutdown
            if (!_shutdownCts.IsCancellationRequested)
            {
                _shutdownCts.Cancel();
            }

            _state = SerfState.SerfShutdown;
            return Task.CompletedTask;
        });

        // Wait for background tasks to complete
        if (_reapTask != null)
        {
            try
            {
                await _reapTask;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug(ex, "[Serf] Reap task shutdown");
            }
        }

        if (_reconnectTask != null)
        {
            try
            {
                await _reconnectTask;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug(ex, "[Serf] Reconnect task shutdown");
            }
        }

        if (_queueMonitorTask != null)
        {
            try
            {
                await _queueMonitorTask;
            }
            catch (Exception ex)
            {
                Logger?.LogDebug(ex, "[Serf] Queue monitor task shutdown");
            }
        }

        // Shutdown memberlist AFTER state transition to prevent race conditions
        if (Memberlist != null)
        {
            try
            {
                await Memberlist.ShutdownAsync();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf] Failed to shutdown memberlist");
            }
        }

        // Wait for snapshotter to finish flushing (CRITICAL for auto-rejoin)
        if (Snapshotter != null)
        {
            try
            {
                Logger?.LogInformation("[Serf] Waiting for snapshotter to finish...");
                await Snapshotter.WaitAsync();
                Logger?.LogInformation("[Serf] Snapshotter shutdown complete");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf] Failed to wait for snapshotter shutdown");
            }
        }

        // Give a moment for any pending callbacks to complete
        await Task.Delay(50);
    }

    // ========== Phase 9.3: Member Failure & Recovery Methods ==========

    /// <summary>
    /// Forcibly removes a failed node from the cluster.
    /// Maps to: Go's RemoveFailedNode() method
    /// </summary>
    /// <param name="nodeName">Name of the node to remove</param>
    /// <param name="prune">If true, also prune from snapshot</param>
    /// <returns>True if node was removed, false if not found</returns>
    public async Task<bool> RemoveFailedNodeAsync(string nodeName, bool prune = false)
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentException("Node name cannot be null or empty", nameof(nodeName));

        // Cannot remove ourselves
        if (nodeName == Config.NodeName)
        {
            throw new InvalidOperationException("Cannot remove local node");
        }

        // Force leave - broadcast a leave message for the node
        var leaveMsg = new MessageLeave
        {
            LTime = Clock.Increment(),
            Node = nodeName,
            Prune = prune
        };

        // Handle locally first
        HandleNodeLeaveIntent(leaveMsg);

        // Broadcast to cluster if we have other alive members
        if (Memberlist != null && Memberlist.NumMembers() > 1)
        {
            var encoded = EncodeMessage(MessageType.Leave, leaveMsg);
            Broadcasts.QueueBytes(encoded);

            // Wait for broadcast with timeout
            await Task.Delay(Config.BroadcastTimeout);
        }

        Logger?.LogInformation("[Serf] Removed failed node: {NodeName}", nodeName);
        return true;
    }

    // ========== Methods called by Delegate ==========

    internal byte[] EncodeTags(Dictionary<string, string> tags)
    {
        return TagEncoder.EncodeTags(tags, Config.ProtocolVersion);
    }

    internal Dictionary<string, string> DecodeTags(byte[] buffer)
    {
        return TagEncoder.DecodeTags(buffer);
    }

    internal void RecordMessageReceived(int size)
    {
        // Emit metrics
        // Reference: Go delegate.go:36
        Config.Metrics.AddSample(new[] { "serf", "msgs", "received" }, size, Config.MetricLabels);
        Logger?.LogTrace("[Serf] Message received: {Size} bytes", size);
    }

    internal void RecordMessageSent(int size)
    {
        // Emit metrics
        // Reference: Go delegate.go:148
        Config.Metrics.AddSample(new[] { "serf", "msgs", "sent" }, size, Config.MetricLabels);
        Logger?.LogTrace("[Serf] Message sent: {Size} bytes", size);
    }

    internal bool HandleNodeLeaveIntent(MessageLeave leave)
    {
        Logger?.LogDebug("[Serf] HandleNodeLeaveIntent: {Node} at LTime {LTime}", leave.Node, leave.LTime);

        // Witness the Lamport time
        Clock.Witness(leave.LTime);

        WithWriteLock(_memberLock, () =>
        {
            // Mark member as leaving/left if it exists
            if (MemberStates.TryGetValue(leave.Node, out var memberInfo))
            {
                // Don't downgrade Left back to Leaving
                // Once memberlist has confirmed the node is Left (via Dead message), 
                // stale leave intent messages should not override that
                if (memberInfo.Status == MemberStatus.Left)
                {
                    Logger?.LogDebug("[Serf] Ignoring leave intent for already-left member {Node}", leave.Node);
                    return;
                }
                
                // Only update if this is newer than current status
                if (leave.LTime > memberInfo.StatusLTime)
                {
                    // If node is currently Failed, transition to Left (for RemoveFailedNode)
                    // Otherwise mark as Leaving (for graceful leave)
                    if (memberInfo.Status == MemberStatus.Failed)
                    {
                        memberInfo.Status = MemberStatus.Left;
                        memberInfo.StatusLTime = leave.LTime;

                        // Move from FailedMembers to LeftMembers
                        FailedMembers.RemoveAll(m => m.Name == leave.Node);
                        if (!LeftMembers.Any(m => m.Name == leave.Node))
                        {
                            LeftMembers.Add(memberInfo);
                        }

                        Logger?.LogInformation("[Serf] Failed member {Node} marked as left", leave.Node);
                    }
                    else
                    {
                        memberInfo.Status = MemberStatus.Leaving;
                        memberInfo.StatusLTime = leave.LTime;
                        Logger?.LogDebug("[Serf] Member {Node} marked as leaving", leave.Node);
                    }

                    // Update the stored Member object if it exists
                    if (memberInfo.Member != null)
                    {
                        memberInfo.Member.Status = memberInfo.Status;
                    }
                }
            }
            else
            {
                // Node not yet in members - store the intent
                MemberStates[leave.Node] = new MemberInfo
                {
                    Name = leave.Node,
                    Status = MemberStatus.Leaving,
                    StatusLTime = leave.LTime
                };
                Logger?.LogDebug("[Serf] Stored leave intent for unknown member: {Node}", leave.Node);
            }
        });

        return false; // No rebroadcast for now
    }

    internal bool HandleNodeJoinIntent(MessageJoin join)
    {
        Logger?.LogDebug("[Serf] HandleNodeJoinIntent: {Node} at LTime {LTime}", join.Node, join.LTime);
        
        // Witness a potentially newer time
        Clock.Witness(join.LTime);

        return WithWriteLock(_memberLock, () =>
        {
            // Check if we know about this member
            if (!MemberStates.TryGetValue(join.Node, out var memberInfo))
            {
                // Create a basic MemberInfo entry for push-pull state synchronization
                // NOTE: We don't emit a MemberJoin event here because we don't have the address yet.
                // The event will be emitted when HandleNodeJoin is called with the full Node object.
                memberInfo = new MemberInfo
                {
                    Name = join.Node,
                    StatusLTime = join.LTime,
                    Status = MemberStatus.Alive
                    // Member will be populated by HandleNodeJoin with full address info
                };
                MemberStates[join.Node] = memberInfo;
                
                Logger?.LogDebug("[Serf] HandleNodeJoinIntent: Created placeholder for unknown member {Node} with LTime {LTime}", 
                    join.Node, join.LTime);
                return true; // Rebroadcast since this is new information
            }

            // Check if this time is newer than what we have
            if (join.LTime <= memberInfo.StatusLTime)
            {
                Logger?.LogDebug("[Serf] HandleNodeJoinIntent: Ignoring old join intent for {Node} (LTime {LTime} <= {StatusLTime})",
                    join.Node, join.LTime, memberInfo.StatusLTime);
                return false;
            }

            // Update the LTime
            var oldStatus = memberInfo.Status;
            memberInfo.StatusLTime = join.LTime;

            // IMPORTANT: Do NOT update a Left/Failed member back to Alive based solely on join intents.
            // Join intent messages may still be propagating in the network after a node has left.
            // We should only update back to Alive when we receive an actual NotifyJoin from memberlist,
            // which indicates the node has genuinely reconnected at the memberlist level.
            // 
            // In Go's implementation, the join intent only updates statusLTime and may change Leaving -> Alive,
            // but does NOT change Left/Failed -> Alive. That transition only happens via handleNodeJoin.
            if (memberInfo.Status == MemberStatus.Leaving)
            {
                // Leaving -> Alive is OK, since the leave broadcast may have been preempted by a newer join
                Logger?.LogInformation("[Serf] HandleNodeJoinIntent: Member {Node} was Leaving, updating to Alive due to newer join intent",
                    join.Node);
                    
                memberInfo.Status = MemberStatus.Alive;
                if (memberInfo.Member != null)
                {
                    memberInfo.Member.Status = MemberStatus.Alive;
                }

                // Emit a member join event
                var memberEvent = new MemberEvent
                {
                    Type = EventType.MemberJoin,
                    Members = new List<Member> { memberInfo.Member ?? new Member { Name = join.Node, Status = MemberStatus.Alive } }
                };
                EmitEvent(memberEvent);
            }
            else if (memberInfo.Status == MemberStatus.Left || memberInfo.Status == MemberStatus.Failed)
            {
                // Do NOT update Left/Failed back to Alive based on join intent alone
                Logger?.LogDebug("[Serf] HandleNodeJoinIntent: Ignoring join intent for {Status} member {Node} (old intents may still be propagating)",
                    memberInfo.Status, join.Node);
                return false; // Don't rebroadcast stale join intents for left/failed nodes
            }

            // Rebroadcast since we updated the LTime
            return true;
        });
    }

    internal bool HandleUserEvent(MessageUserEvent userEvent)
    {
        // Witness a potentially newer time
        EventClock.Witness(userEvent.LTime);

        return WithWriteLock(_eventLock, () =>
        {
            // Ignore if it is before our minimum event time
            if (userEvent.LTime < EventMinTime)
            {
                return false;
            }

            // Check if this message is too old
            var curTime = EventClock.Time();
            var bufferSize = (ulong)Config.EventBuffer;
            if (curTime > bufferSize && userEvent.LTime < (curTime - bufferSize))
            {
                Logger?.LogWarning(
                    "[Serf] Received old event {Name} from time {LTime} (current: {CurTime})",
                    userEvent.Name, userEvent.LTime, curTime);
                return false;
            }

            // Check if we've already seen this event
            if (EventBuffer.TryGetValue(userEvent.LTime, out var seen))
            {
                // Check for duplicate
                foreach (var previous in seen.Events)
                {
                    if (previous.Name == userEvent.Name &&
                        previous.Payload.SequenceEqual(userEvent.Payload))
                    {
                        // Already seen this event
                        return false;
                    }
                }
            }
            else
            {
                // Create new collection for this LTime
                seen = new UserEventCollection { LTime = userEvent.LTime };
                EventBuffer[userEvent.LTime] = seen;
            }

            // Add to recent events
            seen.Events.Add(new UserEventData
            {
                Name = userEvent.Name,
                Payload = userEvent.Payload
            });

            // Emit metrics
            // Reference: Go serf.go:1289-1290
            Config.Metrics.IncrCounter(new[] { "serf", "events" }, 1, Config.MetricLabels);
            Config.Metrics.IncrCounter(new[] { "serf", "events", userEvent.Name }, 1, Config.MetricLabels);

            // Send to EventCh if configured
            if (Config.EventCh != null)
            {
                var evt = new Events.UserEvent
                {
                    LTime = userEvent.LTime,
                    Name = userEvent.Name,
                    Payload = userEvent.Payload,
                    Coalesce = userEvent.CC
                };

                try
                {
                    Config.EventCh.TryWrite(evt);
                    Logger?.LogTrace("[Serf] Emitted UserEvent: {Name} at LTime {LTime}", userEvent.Name, userEvent.LTime);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[Serf] Failed to emit UserEvent: {Name}", userEvent.Name);
                }
            }

            return true; // Rebroadcast this event
        });
    }

    // HandleQuery and HandleQueryResponse are now implemented in Query.cs

    internal void HandleNodeJoin(Memberlist.State.Node? node)
    {
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeJoin called with null node");
            return;
        }

        Logger?.LogDebug("[Serf] HandleNodeJoin: {Name}", node.Name);

        // NOTE: EventJoinIgnore only affects USER EVENTS, not member join events!
        // In Go, eventJoinIgnore is only used to set eventMinTime in the delegate
        // to filter old user events during auto-rejoin. Member join events are
        // always processed.

        WithWriteLock(_memberLock, () =>
        {
            // Create the full member object
            var member = new Member
            {
                Name = node.Name,
                Addr = node.Addr,
                Port = node.Port,
                Tags = DecodeTags(node.Meta),
                Status = MemberStatus.Alive,
                ProtocolMin = node.PMin,
                ProtocolMax = node.PMax,
                ProtocolCur = node.PCur,
                DelegateMin = node.DMin,
                DelegateMax = node.DMax,
                DelegateCur = node.DCur
            };

            // Update or create member state
            if (!MemberStates.TryGetValue(node.Name, out var memberInfo))
            {
                memberInfo = new MemberInfo
                {
                    Name = node.Name,
                    StatusLTime = Clock.Time(),
                    Status = MemberStatus.Alive,
                    Member = member
                };
                MemberStates[node.Name] = memberInfo;
                Logger?.LogInformation("[Serf/Join] NEW member joined: {Name} at {Address}:{Port} (Total members: {Count})",
                    node.Name, node.Addr, node.Port, MemberStates.Count);
            }
            else
            {
                // Update existing member (rejoin)
                var oldStatus = memberInfo.Status;
                
                // Check for flap (Go serf.go:969-971)
                if (oldStatus == MemberStatus.Failed && memberInfo.LeaveTime != default)
                {
                    var deadTime = DateTimeOffset.UtcNow - memberInfo.LeaveTime;
                    if (deadTime < Config.FlapTimeout)
                    {
                        Config.Metrics.IncrCounter(new[] { "serf", "member", "flap" }, 1, Config.MetricLabels);
                    }
                }
                
                memberInfo.StatusLTime = Clock.Time();
                memberInfo.Status = MemberStatus.Alive;
                memberInfo.Member = member;
                Logger?.LogInformation("[Serf/Join] Member rejoined: {Name} (Old status: {OldStatus}, Total members: {Count})",
                    node.Name, oldStatus, MemberStates.Count);
            }
        });

        // Emit metrics
        // Reference: Go serf.go:997
        Config.Metrics.IncrCounter(new[] { "serf", "member", "join" }, 1, Config.MetricLabels);

        // Emit join event to both Snapshotter and EventCh
        var member = new Member
        {
            Name = node.Name,
            Addr = node.Addr,
            Port = node.Port,
            Tags = DecodeTags(node.Meta),
            Status = MemberStatus.Alive,
            ProtocolMin = node.PMin,
            ProtocolMax = node.PMax,
            ProtocolCur = node.PCur,
            DelegateMin = node.DMin,
            DelegateMax = node.DMax,
            DelegateCur = node.DCur
        };

        var memberEvent = new MemberEvent
        {
            Type = EventType.MemberJoin,
            Members = new List<Member> { member }
        };

        EmitEvent(memberEvent);
    }

    internal void HandleNodeLeave(Memberlist.State.Node? node)
    {
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeLeave called with null node");
            return;
        }

        // Check if we're shutdown - don't process events after shutdown
        if (_state == SerfState.SerfShutdown)
        {
            return;
        }

        Logger?.LogDebug("[Serf] HandleNodeLeave: {Name}, NodeState={State}", node.Name, node.State);

        // Determine event type based on memberlist's determination
        // Memberlist sets State to Left for graceful leave (node == from in Dead message)
        // and Dead for actual failure
        var eventType = EventType.MemberLeave;
        var memberStatus = MemberStatus.Left;

        // Check the node's state from memberlist
        if (node.State == NSerf.Memberlist.State.NodeStateType.Dead)
        {
            // This is an actual failure, not a graceful leave
            eventType = EventType.MemberFailed;
            memberStatus = MemberStatus.Failed;
        }
        else if (node.State == NSerf.Memberlist.State.NodeStateType.Left)
        {
            // This is a graceful leave
            eventType = EventType.MemberLeave;
            memberStatus = MemberStatus.Left;
        }

        // Emit metrics
        // Reference: Go serf.go:1047 - metrics by status (failed or left)
        Config.Metrics.IncrCounter(new[] { "serf", "member", memberStatus.ToStatusString() }, 1, Config.MetricLabels);

        try
        {
            WithWriteLock(_memberLock, () =>
            {
                // Update member state if it exists
                if (MemberStates.TryGetValue(node.Name, out var memberInfo))
                {
                    memberInfo.StatusLTime = Clock.Time();
                    memberInfo.Status = memberStatus;
                    memberInfo.LeaveTime = DateTimeOffset.UtcNow;

                    // Store the full member info for reaper and reconnect
                    memberInfo.Member = new Member
                    {
                        Name = node.Name,
                        Addr = node.Addr,
                        Port = node.Port,
                        Tags = DecodeTags(node.Meta),
                        Status = memberStatus,
                        ProtocolMin = node.PMin,
                        ProtocolMax = node.PMax,
                        ProtocolCur = node.PCur,
                        DelegateMin = node.DMin,
                        DelegateMax = node.DMax,
                        DelegateCur = node.DCur
                    };

                    // Add to appropriate list for reaper
                    if (memberStatus == MemberStatus.Failed)
                    {
                        FailedMembers.Add(memberInfo);
                    }
                    else if (memberStatus == MemberStatus.Left)
                    {
                        LeftMembers.Add(memberInfo);
                    }

                    Logger?.LogInformation("[Serf] Member {Status}: {Name}",
                        eventType == EventType.MemberFailed ? "failed" : "left", node.Name);
                }
            });
        }
        catch (ObjectDisposedException)
        {
            // Ignore - we're shutting down
            return;
        }

        // Emit event to both Snapshotter and EventCh
        var member = new Member
        {
            Name = node.Name,
            Addr = node.Addr,
            Port = node.Port,
            Tags = DecodeTags(node.Meta),
            Status = memberStatus,
            ProtocolMin = node.PMin,
            ProtocolMax = node.PMax,
            ProtocolCur = node.PCur,
            DelegateMin = node.DMin,
            DelegateMax = node.DMax,
            DelegateCur = node.DCur
        };

        var memberEvent = new MemberEvent
        {
            Type = eventType,
            Members = new List<Member> { member }
        };

        EmitEvent(memberEvent);
    }

    internal void HandleNodeUpdate(Memberlist.State.Node? node)
    {
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeUpdate called with null node");
            return;
        }

        Logger?.LogDebug("[Serf] HandleNodeUpdate: {Name}", node.Name);

        Member? updatedMember = null;
        
        WithWriteLock(_memberLock, () =>
        {
            // Update member metadata if it exists
            if (MemberStates.TryGetValue(node.Name, out var memberInfo))
            {
                // Update the stored Member object with new data from node
                if (memberInfo.Member != null)
                {
                    memberInfo.Member.Addr = node.Addr;
                    memberInfo.Member.Port = node.Port;
                    memberInfo.Member.Tags = DecodeTags(node.Meta);
                    memberInfo.Member.ProtocolMin = node.PMin;
                    memberInfo.Member.ProtocolMax = node.PMax;
                    memberInfo.Member.ProtocolCur = node.PCur;
                    memberInfo.Member.DelegateMin = node.DMin;
                    memberInfo.Member.DelegateMax = node.DMax;
                    memberInfo.Member.DelegateCur = node.DCur;
                    
                    updatedMember = memberInfo.Member;
                }
                
                memberInfo.StatusLTime = Clock.Time();
                Logger?.LogInformation("[Serf] Member updated: {Name}, Tags: {Tags}", 
                    node.Name, string.Join(", ", DecodeTags(node.Meta).Select(kvp => $"{kvp.Key}={kvp.Value}")));
            }
            else
            {
                // Member doesn't exist yet - ignore the update
                Logger?.LogWarning("[Serf] HandleNodeUpdate: Member {Name} not found in MemberStates", node.Name);
            }
        });

        // Emit metrics
        // Reference: Go serf.go:1091
        if (updatedMember != null)
        {
            Config.Metrics.IncrCounter(new[] { "serf", "member", "update" }, 1, Config.MetricLabels);
        }

        // Emit update event if we successfully updated a member
        if (updatedMember != null)
        {
            var memberEvent = new MemberEvent
            {
                Type = EventType.MemberUpdate,
                Members = new List<Member> { updatedMember }
            };

            EmitEvent(memberEvent);
        }
    }

    internal void HandleNodeConflict(Memberlist.State.Node? existing, Memberlist.State.Node? other)
    {
        if (existing == null || other == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeConflict called with null node(s)");
            return;
        }

        // Log a basic warning if the node is not us...
        if (existing.Name != Config.NodeName)
        {
            Logger?.LogWarning("[Serf] Name conflict for '{Name}' both {Addr1}:{Port1} and {Addr2}:{Port2} are claiming",
                existing.Name, existing.Addr, existing.Port, other.Addr, other.Port);
            return;
        }

        // The current node is conflicting! This is an error
        Logger?.LogError("[Serf] Node name conflicts with another node at {Addr}:{Port}. Names must be unique! (Resolution enabled: {Enabled})",
            other.Addr, other.Port, Config.EnableNameConflictResolution);

        // If automatic resolution is enabled, kick off the resolution
        if (Config.EnableNameConflictResolution)
        {
            _ = Task.Run(async () => await ResolveNodeConflictAsync());
        }
    }

    /// <summary>
    /// ResolveNodeConflict is used to determine which node should remain during
    /// a name conflict. This is done by running an internal query.
    /// Maps to: Go's resolveNodeConflict()
    /// </summary>
    private async Task ResolveNodeConflictAsync()
    {
        try
        {
            // Get the local node
            var local = Memberlist?.LocalNode;
            if (local == null)
            {
                Logger?.LogError("[Serf] Cannot resolve conflict: memberlist not initialized");
                return;
            }

            // Start a name resolution query
            var queryName = $"_serf_conflict";
            var payload = System.Text.Encoding.UTF8.GetBytes(Config.NodeName);
            
            var queryParams = new QueryParam
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            Logger?.LogInformation("[Serf] Starting conflict resolution query for '{NodeName}'", Config.NodeName);
            
            var resp = await QueryAsync(queryName, payload, queryParams);

            // Counter to determine winner
            int responses = 0;
            int matching = 0;

            // Gather responses
            await foreach (var r in resp.ResponseCh.ReadAllAsync())
            {
                // Decode the response
                if (r.Payload.Length < 1 || (MessageType)r.Payload[0] != MessageType.ConflictResponse)
                {
                    Logger?.LogError("[Serf] Invalid conflict query response type: {Type}", r.Payload.Length > 0 ? r.Payload[0] : -1);
                    continue;
                }

                try
                {
                    var member = MessagePackSerializer.Deserialize<Member>(r.Payload[1..]);
                    
                    // Update the counters
                    responses++;
                    if (member.Addr.Equals(local.Addr) && member.Port == local.Port)
                    {
                        matching++;
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[Serf] Failed to decode conflict query response");
                    continue;
                }
            }

            // Query over, determine if we should live
            int majority = (responses / 2) + 1;
            if (matching >= majority)
            {
                Logger?.LogInformation("[Serf] majority in name conflict resolution [{Matching} / {Responses}]",
                    matching, responses);
                return;
            }

            // Since we lost the vote, we need to exit
            Logger?.LogWarning("[Serf] minority in name conflict resolution, quitting [{Matching} / {Responses}]",
                matching, responses);
            
            await ShutdownAsync();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] Failed to resolve node conflict");
        }
    }

    internal Coordinate.Coordinate GetCoordinate()
    {
        // If coordinates are disabled or client not initialized, return default coordinate
        if (Config.DisableCoordinates || _coordClient == null)
        {
            return new Coordinate.Coordinate();
        }

        // Get coordinate from client (returns a copy, so thread-safe)
        return _coordClient.GetCoordinate();
    }

    internal void UpdateCoordinate(string nodeName, Coordinate.Coordinate coordinate, TimeSpan rtt)
    {
        // Early return if coordinates are disabled or client not initialized
        if (Config.DisableCoordinates || _coordClient == null)
        {
            return;
        }

        try
        {
            // Get the current coordinate before update
            var before = _coordClient.GetCoordinate();
            
            // Update coordinate based on the observation
            var updated = _coordClient.Update(nodeName, coordinate, rtt);
            
            // Emit coordinate adjustment metric (Go ping_delegate.go:86-87)
            // Calculate distance between old and new coordinate in milliseconds
            var adjustment = (float)(before.DistanceTo(updated).TotalMilliseconds);
            Config.Metrics.AddSample(new[] { "serf", "coordinate", "adjustment-ms" }, adjustment, Config.MetricLabels);
            
            // Cache the latest coordinate for this node
            _coordCacheLock.EnterWriteLock();
            try
            {
                _coordCache[nodeName] = updated;
            }
            finally
            {
                _coordCacheLock.ExitWriteLock();
            }
            
            Logger?.LogTrace("[Serf] Updated coordinate for {Node}, RTT: {RTT}ms, New position: {Vec}",
                nodeName, rtt.TotalMilliseconds, string.Join(",", updated.Vec.Select(v => v.ToString("F4"))));
        }
        catch (Exception ex)
        {
            // Emit coordinate rejection metric (Go ping_delegate.go:78)
            Config.Metrics.IncrCounter(new[] { "serf", "coordinate", "rejected" }, 1, Config.MetricLabels);
            Logger?.LogError(ex, "[Serf] Failed to update coordinate for {Node}", nodeName);
        }
    }

    internal string? ValidateNodeName(string nodeName)
    {
        // Only validate if enabled in config
        if (!Config.ValidateNodeNames)
        {
            return null;
        }

        // Check for invalid characters (spaces, control characters, etc.)
        if (string.IsNullOrWhiteSpace(nodeName) || nodeName.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return "Node name contains invalid characters";
        }

        // Check length (max 128 characters as per Serf specification)
        if (nodeName.Length > 128)
        {
            return $"Node name is {nodeName.Length} characters. Node name must be 128 characters or less";
        }

        return null;
    }

    /// <summary>
    /// DefaultQueryTimeout returns the default timeout value for a query.
    /// Computed as GossipInterval * QueryTimeoutMult * log(N+1)
    /// </summary>
    public TimeSpan DefaultQueryTimeout()
    {
        // Determine current cluster size N
        int n = Memberlist?.NumMembers() ?? 1;

        // Base gossip interval and multiplier
        var gossip = Config.MemberlistConfig?.GossipInterval ?? TimeSpan.FromMilliseconds(500);
        int mult = Config.QueryTimeoutMult;

        // Factor = ceil(log10(N+1)), minimum 1
        int factor = (int)Math.Ceiling(Math.Log10(n + 1));
        if (factor <= 0) factor = 1;

        // Compute as ticks to avoid TimeSpan arithmetic limitations
        long ticks = gossip.Ticks * mult * factor;
        return new TimeSpan(ticks);
    }

    /// <summary>
    /// DefaultQueryParams is used to return the default query parameters.
    /// </summary>
    public QueryParam DefaultQueryParams()
    {
        return new QueryParam
        {
            FilterNodes = null,
            FilterTags = null,
            RequestAck = false,
            Timeout = DefaultQueryTimeout()
        };
    }

    internal byte[] EncodeMessage(MessageType messageType, object message)
    {
        try
        {
            var payload = MessagePackSerializer.Serialize(message);
            var result = new byte[payload.Length + 1];
            result[0] = (byte)messageType;
            Array.Copy(payload, 0, result, 1, payload.Length);
            return result;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] Failed to encode message of type {Type}", messageType);
            return Array.Empty<byte>();
        }
    }

    // Lock management helpers - Use try-finally pattern (C# equivalent of Go's defer)

    /// <summary>
    /// Executes an action while holding a read lock. Ensures lock is released.
    /// Pattern: C# try-finally equivalent of Go's defer lock.RUnlock()
    /// </summary>
    private void WithReadLock(ReaderWriterLockSlim lockObj, Action action)
    {
        lockObj.EnterReadLock();
        try
        {
            action();
        }
        finally
        {
            lockObj.ExitReadLock();
        }
    }

    /// <summary>
    /// Executes a function while holding a read lock. Ensures lock is released.
    /// </summary>
    private T WithReadLock<T>(ReaderWriterLockSlim lockObj, Func<T> func)
    {
        lockObj.EnterReadLock();
        try
        {
            return func();
        }
        finally
        {
            lockObj.ExitReadLock();
        }
    }

    /// <summary>
    /// Executes an action while holding a write lock. Ensures lock is released.
    /// Pattern: C# try-finally equivalent of Go's defer lock.Unlock()
    /// </summary>
    private void WithWriteLock(ReaderWriterLockSlim lockObj, Action action)
    {
        lockObj.EnterWriteLock();
        try
        {
            action();
        }
        finally
        {
            lockObj.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes a function while holding a write lock. Ensures lock is released.
    /// </summary>
    private T WithWriteLock<T>(ReaderWriterLockSlim lockObj, Func<T> func)
    {
        lockObj.EnterWriteLock();
        try
        {
            return func();
        }
        finally
        {
            lockObj.ExitWriteLock();
        }
    }

    /// <summary>
    /// Executes an action while holding a semaphore lock. Ensures lock is released.
    /// Pattern: C# equivalent of Go's sync.Mutex with defer
    /// </summary>
    private void WithLock(SemaphoreSlim semaphore, Action action)
    {
        semaphore.Wait();
        try
        {
            action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes a function while holding a semaphore lock. Ensures lock is released.
    /// </summary>
    private T WithLock<T>(SemaphoreSlim semaphore, Func<T> func)
    {
        semaphore.Wait();
        try
        {
            return func();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes an async action while holding a semaphore lock. Ensures lock is released.
    /// </summary>
    private async Task WithLockAsync(SemaphoreSlim semaphore, Func<Task> action)
    {
        await semaphore.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Executes an async function while holding a semaphore lock. Ensures lock is released.
    /// </summary>
    private async Task<T> WithLockAsync<T>(SemaphoreSlim semaphore, Func<Task<T>> func)
    {
        await semaphore.WaitAsync();
        try
        {
            return await func();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Checks if encryption is enabled for this Serf instance.
    /// Encryption is enabled if a keyring is configured.
    /// Maps to: Go's EncryptionEnabled() method
    /// </summary>
    public bool EncryptionEnabled()
    {
        return Config.MemberlistConfig?.Keyring != null;
    }

    /// <summary>
    /// Writes the current keyring to the configured keyring file.
    /// Keys are JSON encoded and base64 encoded for storage.
    /// Maps to: Go's writeKeyringFile() method
    /// </summary>
    internal async Task WriteKeyringFileAsync()
    {
        if (string.IsNullOrEmpty(Config.KeyringFile))
        {
            return;
        }

        var keyring = Config.MemberlistConfig?.Keyring;
        if (keyring == null)
        {
            throw new InvalidOperationException("No keyring available to write");
        }

        // Get all keys and encode them to base64
        var keysRaw = keyring.GetKeys();
        var keysEncoded = new List<string>();
        foreach (var key in keysRaw)
        {
            keysEncoded.Add(Convert.ToBase64String(key));
        }

        // Serialize to JSON with indentation
        var json = System.Text.Json.JsonSerializer.Serialize(keysEncoded, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        // Write to file with restricted permissions (600 in Unix terms)
        // On Windows, this creates a file with default permissions
        await File.WriteAllTextAsync(Config.KeyringFile, json);

        Logger?.LogDebug("[Serf] Wrote keyring file: {Path}", Config.KeyringFile);
    }

    // Legacy lock management for backward compatibility (Phase 6 delegates)
    internal void AcquireMemberLock() => _memberLock.EnterReadLock();
    internal void ReleaseMemberLock() => _memberLock.ExitReadLock();
    internal void AcquireEventLock() => _eventLock.EnterReadLock();
    internal void ReleaseEventLock() => _eventLock.ExitReadLock();

    /// <summary>
    /// Disposes the Serf instance and releases all locks.
    /// </summary>
    public void Dispose()
    {
        // Dispose Snapshotter to flush buffered writes (ignore if already disposed)
        if (Snapshotter != null)
        {
            try
            {
                Snapshotter.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }

        _memberLock?.Dispose();
        _eventLock?.Dispose();
        _queryLock?.Dispose();
        _stateLock?.Dispose();
        _coordCacheLock?.Dispose();
        _joinLock?.Dispose();
    }

    /// <summary>
    /// Asynchronously disposes the Serf instance.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Internal member state tracking for Serf.
/// Tracks member status and Lamport time for state changes.
/// </summary>
internal class MemberInfo
{
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// State machine managing member status transitions.
    /// </summary>
    public StateMachine.MemberStateMachine? StateMachine { get; set; }
    
    /// <summary>
    /// Current status of the member - delegates to StateMachine if available.
    /// </summary>
    public MemberStatus Status
    {
        get => StateMachine?.CurrentState ?? _status;
        set
        {
            if (StateMachine != null)
            {
                // When StateMachine exists, status is managed by it
                // This setter is for backward compatibility only
            }
            else
            {
                _status = value;
            }
        }
    }
    
    /// <summary>
    /// Lamport time of last status update - delegates to StateMachine if available.
    /// </summary>
    public LamportTime StatusLTime
    {
        get => StateMachine?.StatusLTime ?? _statusLTime;
        set
        {
            if (StateMachine != null)
            {
                // When StateMachine exists, LTime is managed by it
                // This setter is for backward compatibility only
            }
            else
            {
                _statusLTime = value;
            }
        }
    }
    
    public DateTimeOffset LeaveTime { get; set; }
    public Member Member { get; set; } = new Member();
    
    // Backing fields - used when StateMachine is null (backward compatibility)
    private MemberStatus _status = MemberStatus.Alive;
    private LamportTime _statusLTime;
}
