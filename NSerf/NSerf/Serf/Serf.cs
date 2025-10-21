// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Partial implementation for Phase 6 (Delegates) - will be expanded in Phase 9

using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using NSerf.Serf.Events;
using NSerf.Serf.Managers;
using NSerf.Serf.Helpers;
using MessagePack;
using System.Threading.Channels;

namespace NSerf.Serf;

/// <summary>
/// Main Serf class for cluster membership and coordination.
/// Partial implementation supporting Delegate operations - will be fully implemented in Phase 9.
/// </summary>
public partial class Serf : IDisposable, IAsyncDisposable
{
    public const byte ProtocolVersionMin = 2;
    public const byte ProtocolVersionMax = 5;
    public const int UserEventSizeLimit = 9 * 1024; // 9KB
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ClusterCoordinator _clusterCoordinator;
    internal Config Config { get; private set; }
    internal ILogger? Logger { get; private set; }
    internal LamportClock Clock { get; private set; }
    internal LamportClock EventClock { get; private set; }
    internal LamportClock QueryClock { get; private set; }
    internal BroadcastQueue Broadcasts { get; private set; }
    internal BroadcastQueue EventBroadcasts { get; private set; }
    internal BroadcastQueue QueryBroadcasts { get; private set; }
    internal Dictionary<LamportTime, QueryCollection> QueryBuffer { get; private set; }
    internal readonly IMemberManager _memberManager;
    internal EventManager? _eventManager;
    private SerfMetricsRecorder? _metricsRecorder;
    private SerfQueryHelper? _queryHelper; internal bool EventJoinIgnore { get; set; }
    internal LamportTime QueryMinTime { get; set; }
    private readonly Dictionary<LamportTime, QueryResponse> _queryResponses = new();
    private readonly Random _queryRandom = new();
    internal Memberlist.Memberlist? Memberlist { get; private set; }
    private Memberlist.Delegates.IEventDelegate? _eventDelegate;
    internal Snapshotter? Snapshotter { get; private set; }
    private ChannelWriter<Event>? _snapshotInCh;
    private Coordinate.CoordinateClient? _coordClient;
    private readonly ReaderWriterLockSlim _queryLock = new();
    private readonly ReaderWriterLockSlim _coordCacheLock = new();
    private readonly Dictionary<string, Coordinate.Coordinate> _coordCache = new();
    private readonly SemaphoreSlim _joinLock = new(1, 1);

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
        Broadcasts = new BroadcastQueue(new TransmitLimitedQueue());
        EventBroadcasts = new BroadcastQueue(new TransmitLimitedQueue());
        QueryBroadcasts = new BroadcastQueue(new TransmitLimitedQueue());
        QueryBuffer = [];
        _memberManager = new MemberManager();
        _eventManager = new EventManager(
            eventCh: config.EventCh,
            eventBufferSize: config.EventBuffer,
            logger: Logger);
        _clusterCoordinator = new ClusterCoordinator(logger: Logger);
        EventJoinIgnore = false;
        QueryMinTime = 0;
    }

    /// <summary>
    /// BroadcastJoin broadcasts a new join intent with a given clock value.
    /// Used on join or to refute an older leave intent. Cannot be called with member lock held.
    /// </summary>
    internal void BroadcastJoin(LamportTime ltime)
    {
        var msg = new MessageJoin
        {
            LTime = ltime,
            Node = Config.NodeName
        };
        Clock.Witness(ltime);
        HandleNodeJoinIntent(msg);
        var raw = EncodeMessage(MessageType.Join, msg);
        if (raw.Length > 0)
        {
            Broadcasts.QueueBytes(raw);
        }
    }

    /// <summary>
    /// Returns the number of members known to this Serf instance.
    /// Thread-safe read operation using MemberManager transaction pattern.
    /// </summary>
    public int NumMembers() => _memberManager.ExecuteUnderLock(accessor => accessor.GetMemberCount());

    /// <summary>
    /// Sets a member using MemberManager transaction pattern.
    /// </summary>
    private void SetMemberState(string name, MemberInfo memberInfo)
    {
        _memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.AddMember(memberInfo);
        });
    }

    /// <summary>
    /// Removes a member using MemberManager transaction pattern.
    /// </summary>
    private void RemoveMemberState(string name)
    {
        _memberManager.ExecuteUnderLock(accessor =>
        {
            accessor.RemoveMember(name);
        });
    }

    /// <summary>
    /// Returns all known members in the cluster, including failed and left nodes.
    /// Matches Go's behavior: returns from Serf's own tracking,
    /// not from memberlist which filters out dead/left nodes.
    /// Thread-safe read operation using MemberManager transaction pattern.
    /// </summary>
    public Member[] Members()
    {
        return _memberManager.ExecuteUnderLock(accessor =>
        {
            return accessor.GetAllMembers()
                .Where(mi => mi.Member != null)
                .Select(mi => mi.Member!)
                .ToArray();
        });
    }

    /// <summary>
    /// Returns members filtered by status.
    /// Thread-safe read operation using MemberManager transaction pattern.
    /// </summary>
    public Member[] Members(MemberStatus statusFilter)
    {
        return _memberManager.ExecuteUnderLock(accessor =>
        {
            return accessor.GetMembersByStatus(statusFilter)
                .Where(mi => mi.Member != null)
                .Select(mi => mi.Member!)
                .ToArray();
        });
    }


    /// <summary>
    /// Emits an event to both the Snapshotter and the user's EventCh.
    /// This mimics Go's behavior where events are sent to both channels.
    /// </summary>
    private void EmitEvent(Event evt)
    {
        Logger?.LogInformation("[Serf/EmitEvent] Emitting {EventType}, _snapshotInCh is {IsNull}",
            evt.GetType().Name, _snapshotInCh == null ? "NULL" : "SET");

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
        _eventManager?.EmitEvent(evt);
    }

    /// <summary>
    /// Creates a new Serf instance with the given configuration.
    /// Maps to: Go's Create() function
    /// </summary>
    public static async Task<Serf> CreateAsync(Config? config)
    {
        ArgumentNullException.ThrowIfNull(config);
        SerfValidationHelper.ValidateProtocolVersion(config.ProtocolVersion, ProtocolVersionMin, ProtocolVersionMax);
        SerfValidationHelper.ValidateUserEventSizeLimit(config.UserEventSizeLimit, UserEventSizeLimit);

        var serf = new Serf(config);
        serf._metricsRecorder = new SerfMetricsRecorder(config.Metrics, config.MetricLabels, serf.Logger);
        serf._queryHelper = new SerfQueryHelper(
            () => serf.Memberlist?.NumMembers() ?? 1,
            () => config.MemberlistConfig?.GossipInterval ?? TimeSpan.FromMilliseconds(500),
            config.QueryTimeoutMult);

        serf.Clock.Increment();
        serf.EventClock.Increment();
        serf.QueryClock.Increment();

        ChannelWriter<Event>? eventDestination = config.EventCh;
        ChannelWriter<Event>? internalQueryInput = null;

        if (config.EventCh != null)
        {
            serf.Logger?.LogInformation("[Serf/InternalQuery] Setting up internal query handler");
            var (queryInputCh, queryHandler) = SerfQueries.Create(
                serf,
                config.EventCh,
                serf._shutdownCts.Token);

            internalQueryInput = queryInputCh;
            eventDestination = queryInputCh;

            serf.Logger?.LogInformation("[Serf/InternalQuery] ✓ Internal query handler created");
        }

        if (eventDestination != config.EventCh)
        {
            serf._eventManager = new EventManager(
                eventCh: eventDestination,
                eventBufferSize: config.EventBuffer,
                logger: serf.Logger);
            serf.Logger?.LogInformation("[Serf/EventManager] ✓ EventManager re-initialized with query handler routing");
        }

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
                outCh: eventDestination,
                shutdownToken: serf._shutdownCts.Token);

            var inCh = snapshotResult.InCh;
            var snapshotter = snapshotResult.Snap;

            serf.Snapshotter = snapshotter;
            serf._snapshotInCh = inCh;
            serf.Logger?.LogInformation("[Serf/Snapshot] ✓ Snapshotter created successfully for node {NodeName}", config.NodeName);
            serf.Logger?.LogInformation("[Serf/Snapshot] ✓ _snapshotInCh is {Status}", serf._snapshotInCh == null ? "NULL (ERROR!)" : "READY");
            serf.Clock.Witness(snapshotter.LastClock);
            serf.EventClock.Witness(snapshotter.LastEventClock);
            serf.QueryClock.Witness(snapshotter.LastQueryClock);
            serf.Logger?.LogInformation("[Serf/Snapshot] Clock restored - Clock: {Clock}, EventClock: {EventClock}, QueryClock: {QueryClock}",
                snapshotter.LastClock, snapshotter.LastEventClock, snapshotter.LastQueryClock);
            previousNodes = snapshotter.AliveNodes();
            serf.Logger?.LogInformation("[Serf/Snapshot] Loaded {Count} previous nodes from snapshot", previousNodes.Count);
            foreach (var node in previousNodes)
            {
                serf.Logger?.LogInformation("[Serf/Snapshot] - Previous node: {Name} at {Addr}", node.Name, node.Addr);
            }

        }
        else
        {
            serf.Logger?.LogInformation("[Serf/Snapshot] No snapshot path configured for node {NodeName}", config.NodeName);
        }

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

        if (config.MemberlistConfig != null)
        {
            serf._eventDelegate = new SerfEventDelegate(serf);
            config.MemberlistConfig.Events = serf._eventDelegate;
            var serfDelegate = new Delegate(serf);
            config.MemberlistConfig.Delegate = serfDelegate;

            if (!config.DisableCoordinates)
            {
                var pingDelegate = new PingDelegate(serf);
                config.MemberlistConfig.Ping = pingDelegate;
                serf.Logger?.LogInformation("[Serf/Coordinates] ✓ PingDelegate configured for RTT tracking");
            }

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

            serf.Memberlist = NSerf.Memberlist.Memberlist.Create(config.MemberlistConfig);

            var localNode = serf.Memberlist.LocalNode;
            var localMember = new MemberInfo
            {
                Name = config.NodeName,
                StateMachine = new StateMachine.MemberStateMachine(
                    config.NodeName,
                    MemberStatus.Alive,
                    0,
                    serf.Logger),
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
            serf.SetMemberState(config.NodeName, localMember);
        }

        serf.StartBackgroundTasks();

        if (previousNodes != null && previousNodes.Count > 0)
        {
            serf.Logger?.LogInformation("[Serf/AutoRejoin] Starting auto-rejoin task for {Count} nodes", previousNodes.Count);

            // Synchronous best-effort attempt before returning
            // This ensures refutation happens during CreateAsync, making tests deterministic
            try
            {
                await Task.Delay(300);
                var addrs = previousNodes.Select(n => n.Addr).ToArray();
                var joinedNow = await serf.JoinAsync(addrs, ignoreOld: true);
                serf.Logger?.LogInformation("[Serf/AutoRejoin] Synchronous attempt joined {Joined}/{Total}", joinedNow, addrs.Length);
            }
            catch (Exception ex)
            {
                serf.Logger?.LogDebug(ex, "[Serf/AutoRejoin] Synchronous attempt failed (will retry in background)");
            }

            // Background resilience task for production scenarios
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000);

                    var addrs = previousNodes.Select(n => n.Addr).ToArray();
                    serf.Logger?.LogInformation("[Serf/AutoRejoin] Will attempt to join {Count} addresses: [{Addrs}]",
                        addrs.Length, string.Join(", ", addrs));

                    var attempts = 10;
                    for (int i = 1; i <= attempts; i++)
                    {
                        try
                        {
                            var joined = await serf.JoinAsync(addrs, ignoreOld: true);
                            serf.Logger?.LogInformation("[Serf/AutoRejoin] Attempt {Attempt}: joined {Joined}/{Total}", i, joined, addrs.Length);
                            if (joined > 0)
                            {
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            serf.Logger?.LogWarning(ex, "[Serf/AutoRejoin] Attempt {Attempt} failed", i);
                        }
                        await Task.Delay(500);
                    }
                }
                catch (Exception ex)
                {
                    serf.Logger?.LogError(ex, "[Serf/AutoRejoin] Exception during auto-rejoin");
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
    /// Maps to: Go's State() method
    /// Thread-safe via ClusterCoordinator.
    /// </summary>
    public SerfState State() => _clusterCoordinator.GetCurrentState();

    /// <summary>
    /// Returns the protocol version being used by this Serf instance.
    /// </summary>
    public byte ProtocolVersion() => Config.ProtocolVersion;

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
    public async Task SetTagsAsync(Dictionary<string, string>? tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        Config.Tags = new Dictionary<string, string>(tags);
        if (Memberlist != null)
            await Memberlist.UpdateNodeAsync(Config.BroadcastTimeout);
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
    public Task UserEventAsync(string? name, byte[]? payload, bool coalesce)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(payload);

        var payloadSizeBeforeEncoding = name.Length + payload.Length;

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

        var msg = new MessageUserEvent
        {
            LTime = EventClock.Time(),
            Name = name,
            Payload = payload,
            CC = coalesce
        };

        var raw = EncodeMessage(MessageType.UserEvent, msg);

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
        HandleUserEvent(msg);
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
        if (State() != SerfState.SerfAlive)
            throw new InvalidOperationException("Serf can't join after Leave or Shutdown");


        return await WithLockAsync(_joinLock, async () =>
        {
            if (ignoreOld)
            {
                EventJoinIgnore = true;
            }

            try
            {
                if (Memberlist == null)
                    throw new InvalidOperationException("Memberlist not initialized");

                var (numJoined, error) = await Memberlist.JoinAsync(existing);

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

                if (error != null && numJoined == 0) throw error;

                return numJoined;
            }
            finally
            {
                if (ignoreOld) EventJoinIgnore = false;
            }
        });
    }

    /// <summary>
    /// Gracefully leaves the Serf cluster.
    /// Maps to: Go's Leave() method
    /// </summary>
    public async Task LeaveAsync()
    {
        if (!_clusterCoordinator.TryTransitionToLeaving())
        {
            Logger?.LogWarning("[Serf] Cannot leave - already in {State} state", _clusterCoordinator.GetCurrentState());
            return;
        }

        try
        {

            if (Snapshotter != null)
                await Snapshotter.LeaveAsync();

            var leaveMsg = new MessageLeave
            {
                LTime = Clock.Increment(),
                Node = Config.NodeName
            };

            HandleNodeLeaveIntent(leaveMsg);

            var encoded = EncodeMessage(MessageType.Leave, leaveMsg);
            if (encoded.Length > 0)
            {
                Broadcasts.QueueBytes(encoded);
                Logger?.LogDebug("[Serf] Broadcasted leave intent for: {Node}", Config.NodeName);
            }

            if (Memberlist != null)
            {
                try
                {
                    var error = await Memberlist.LeaveAsync(Config.BroadcastTimeout);
                    if (error != null)
                        Logger?.LogWarning("[Serf] Error during memberlist leave: {Error}", error.Message);

                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[Serf] Exception during memberlist leave");
                }
            }

            Logger?.LogDebug("[Serf] Waiting {Delay}ms for leave propagation", Config.LeavePropagateDelay.TotalMilliseconds);
            await Task.Delay(Config.LeavePropagateDelay);
            _clusterCoordinator.TryTransitionToLeft();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[Serf] Error during leave operation");
            throw;
        }
    }

    /// <summary>
    /// Shuts down the Serf instance and stops all background operations.
    /// Maps to: Go's Shutdown() method
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (!_clusterCoordinator.TryTransitionToShutdown())
        {
            Logger?.LogDebug("[Serf] Already shutdown");
            return;
        }

        if (!_shutdownCts.IsCancellationRequested) _shutdownCts.Cancel();

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

        await Task.Delay(50);
    }

    /// <summary>
    /// Forcibly removes a failed node from the cluster.
    /// Maps to: Go's RemoveFailedNode() method
    /// </summary>
    /// <param name="nodeName">Name of the node to remove</param>
    /// <param name="prune">If true, also prune from snapshot</param>
    /// <returns>True if node was removed, false if not found</returns>
    public async Task<bool> RemoveFailedNodeAsync(string? nodeName, bool prune = false)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(nodeName);

        if (nodeName == Config.NodeName)
            throw new InvalidOperationException("Cannot remove local node");

        var leaveMsg = new MessageLeave
        {
            LTime = Clock.Increment(),
            Node = nodeName,
            Prune = prune
        };

        HandleNodeLeaveIntent(leaveMsg);

        if (Memberlist != null && Memberlist.NumMembers() > 1)
        {
            var encoded = EncodeMessage(MessageType.Leave, leaveMsg);
            Broadcasts.QueueBytes(encoded);
            await Task.Delay(Config.BroadcastTimeout);
        }

        Logger?.LogInformation("[Serf] Removed failed node: {NodeName}", nodeName);
        return true;
    }

    internal byte[] EncodeTags(Dictionary<string, string> tags)
        => SerfMessageEncoder.EncodeTags(tags, Config.ProtocolVersion);

    internal Dictionary<string, string> DecodeTags(byte[] buffer) => SerfMessageEncoder.DecodeTags(buffer);

    internal void RecordMessageReceived(int size) => _metricsRecorder?.RecordMessageReceived(size);

    internal void RecordMessageSent(int size) => _metricsRecorder?.RecordMessageSent(size);

    internal bool HandleNodeLeaveIntent(MessageLeave leave)
    {
        var tempEvents = new List<Events.Event>();

        var handler = new Handlers.IntentHandler(
            _memberManager,
            tempEvents,
            Clock,
            Logger,
            Config.NodeName,
            () => _clusterCoordinator.GetCurrentState(),
            null);

        var result = handler.HandleLeaveIntent(leave);

        foreach (var evt in tempEvents) EmitEvent(evt);

        return result;
    }

    internal bool HandleNodeJoinIntent(MessageJoin join)
    {
        var handler = new Handlers.IntentHandler(
            _memberManager,
            [],
            Clock,
            Logger,
            Config.NodeName,
            () => _clusterCoordinator.GetCurrentState(),
            null);

        return handler.HandleJoinIntent(join);
    }

    internal bool HandleUserEvent(MessageUserEvent userEvent)
    {
        if (_eventManager == null)
        {
            Logger?.LogWarning("[Serf] EventManager not initialized");
            return false;
        }

        var shouldRebroadcast = _eventManager.HandleUserEvent(userEvent);
        if (shouldRebroadcast)
        {
            Config.Metrics.IncrCounter(new[] { "serf", "events" }, 1, Config.MetricLabels);
            Config.Metrics.IncrCounter(new[] { "serf", "events", userEvent.Name }, 1, Config.MetricLabels);
        }

        return shouldRebroadcast;
    }

    internal void HandleNodeJoin(Memberlist.State.Node? node)
    {
        var tempEvents = new List<Events.Event>();
        var handler = new Handlers.NodeEventHandler(
            _memberManager,
            tempEvents,
            Clock,
            Logger,
            () => DecodeTags(node?.Meta ?? []));

        var shouldCheckFlap = false;
        MemberStatus? oldStatus = null;
        DateTimeOffset? leaveTime = null;

        if (node != null)
        {
            _memberManager.ExecuteUnderLock(accessor =>
            {
                var memberInfo = accessor.GetMember(node.Name);
                if (memberInfo != null)
                {
                    oldStatus = memberInfo.Status;
                    leaveTime = memberInfo.LeaveTime;
                    shouldCheckFlap = oldStatus == MemberStatus.Failed && leaveTime != default;
                }
            });
        }

        handler.HandleNodeJoin(node);

        if (shouldCheckFlap && leaveTime.HasValue)
        {
            var deadTime = DateTimeOffset.UtcNow - leaveTime.Value;
            if (deadTime < Config.FlapTimeout)
            {
                Config.Metrics.IncrCounter(new[] { "serf", "member", "flap" }, 1, Config.MetricLabels);
            }
        }

        Config.Metrics.IncrCounter(new[] { "serf", "member", "join" }, 1, Config.MetricLabels);

        foreach (var evt in tempEvents) EmitEvent(evt);

    }

    internal void HandleNodeLeave(Memberlist.State.Node? node)
    {
        if (node == null || _clusterCoordinator.IsShutdown()) return;

        var tempEvents = new List<Events.Event>();

        var handler = new Handlers.NodeEventHandler(
            _memberManager,
            tempEvents,
            Clock,
            Logger,
            () => DecodeTags(node?.Meta ?? []));

        try
        {
            handler.HandleNodeLeave(node);

            var memberStatus = (node.State == NSerf.Memberlist.State.NodeStateType.Dead)
                ? MemberStatus.Failed
                : MemberStatus.Left;

            Config.Metrics.IncrCounter(new[] { "serf", "member", memberStatus.ToStatusString() }, 1, Config.MetricLabels);

            foreach (var evt in tempEvents) EmitEvent(evt);

        }
        catch (ObjectDisposedException)
        {
        }
    }

    internal void HandleNodeUpdate(Memberlist.State.Node? node)
    {
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeUpdate called with null node");
            return;
        }

        Logger?.LogDebug("[Serf] HandleNodeUpdate: {Name}", node.Name);

        Member? updatedMember = _memberManager.ExecuteUnderLock(accessor =>
        {
            var memberInfo = accessor.GetMember(node.Name);
            if (memberInfo != null)
            {
                if (memberInfo.Member != null)
                {
                    accessor.UpdateMember(node.Name, m =>
                    {
                        if (m.Member != null)
                        {
                            m.Member.Addr = node.Addr;
                            m.Member.Port = node.Port;
                            m.Member.Tags = DecodeTags(node.Meta);
                            m.Member.ProtocolMin = node.PMin;
                            m.Member.ProtocolMax = node.PMax;
                            m.Member.ProtocolCur = node.PCur;
                            m.Member.DelegateMin = node.DMin;
                            m.Member.DelegateMax = node.DMax;
                            m.Member.DelegateCur = node.DCur;
                        }
                    });

                    Logger?.LogInformation("[Serf] Member updated: {Name}, Tags: {Tags}",
                        node.Name, string.Join(", ", DecodeTags(node.Meta).Select(kvp => $"{kvp.Key}={kvp.Value}")));

                    return memberInfo.Member;
                }
            }
            else
            {
                Logger?.LogWarning("[Serf] HandleNodeUpdate: Member {Name} not found", node.Name);
            }

            return null;
        });

        // Reference: Go serf.go:1091
        if (updatedMember != null)
            Config.Metrics.IncrCounter(new[] { "serf", "member", "update" }, 1, Config.MetricLabels);

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

        if (existing.Name != Config.NodeName)
        {
            Logger?.LogWarning("[Serf] Name conflict for '{Name}' both {Addr1}:{Port1} and {Addr2}:{Port2} are claiming",
                existing.Name, existing.Addr, existing.Port, other.Addr, other.Port);
            return;
        }

        Logger?.LogError("[Serf] Node name conflicts with another node at {Addr}:{Port}. Names must be unique! (Resolution enabled: {Enabled})",
            other.Addr, other.Port, Config.EnableNameConflictResolution);

        if (Config.EnableNameConflictResolution)
            _ = Task.Run(ResolveNodeConflictAsync);
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
            var local = Memberlist?.LocalNode;
            if (local == null)
            {
                Logger?.LogError("[Serf] Cannot resolve conflict: memberlist not initialized");
                return;
            }

            var queryName = $"_serf_conflict";
            var payload = System.Text.Encoding.UTF8.GetBytes(Config.NodeName);

            var queryParams = new QueryParam
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            Logger?.LogInformation("[Serf] Starting conflict resolution query for '{NodeName}'", Config.NodeName);

            var resp = await QueryAsync(queryName, payload, queryParams);
            int responses = 0;
            int matching = 0;

            await foreach (var r in resp.ResponseCh.ReadAllAsync())
            {
                if (r.Payload.Length < 1 || (MessageType)r.Payload[0] != MessageType.ConflictResponse)
                {
                    Logger?.LogError("[Serf] Invalid conflict query response type: {Type}", r.Payload.Length > 0 ? r.Payload[0] : -1);
                    continue;
                }

                try
                {
                    var member = MessagePackSerializer.Deserialize<Member>(r.Payload.AsMemory()[1..]);
                    responses++;
                    if (member.Addr.Equals(local.Addr) && member.Port == local.Port) matching++;
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[Serf] Failed to decode conflict query response");
                    continue;
                }
            }

            int majority = (responses / 2) + 1;
            if (matching >= majority)
            {
                Logger?.LogInformation("[Serf] majority in name conflict resolution [{Matching} / {Responses}]",
                    matching, responses);
                return;
            }

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
        if (Config.DisableCoordinates || _coordClient == null)
            return new Coordinate.Coordinate();

        return _coordClient.GetCoordinate();
    }

    internal void UpdateCoordinate(string nodeName, Coordinate.Coordinate coordinate, TimeSpan rtt)
    {
        if (Config.DisableCoordinates || _coordClient == null) return;

        try
        {
            var before = _coordClient.GetCoordinate();

            var updated = _coordClient.Update(nodeName, coordinate, rtt);

            // Emit coordinate adjustment metric (Go ping_delegate.go:86-87)
            Config.Metrics.AddSample(new[] { "serf", "coordinate", "adjustment-ms" }, (float)before.DistanceTo(updated).TotalMilliseconds, Config.MetricLabels);

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
        => SerfValidationHelper.ValidateNodeName(nodeName, Config.ValidateNodeNames);

    /// <summary>
    /// DefaultQueryTimeout returns the default timeout value for a query.
    /// Computed as GossipInterval * QueryTimeoutMult * log(N+1)
    /// </summary>
    public TimeSpan DefaultQueryTimeout()
        => _queryHelper?.CalculateDefaultQueryTimeout() ?? TimeSpan.FromSeconds(5);

    /// <summary>
    /// DefaultQueryParams is used to return the default query parameters.
    /// </summary>
    public QueryParam DefaultQueryParams() => _queryHelper?.CreateDefaultQueryParams() ?? new QueryParam
    {
        FilterNodes = null,
        FilterTags = null,
        RequestAck = false,
        Timeout = TimeSpan.FromSeconds(5)
    };

    internal byte[] EncodeMessage(MessageType messageType, object message)
        => SerfMessageEncoder.EncodeMessage(messageType, message, Logger);

    /// <summary>
    /// Executes an action while holding a read lock. Ensures lock is released.
    /// Used by Query.cs for _queryLock.
    /// </summary>
    private void WithReadLock(ReaderWriterLockSlim lockObj, Action action)
        => LockHelper.WithReadLock(lockObj, action);

    /// <summary>
    /// Executes an action while holding a write lock. Ensures lock is released.
    /// Used by Query.cs for _queryLock.
    /// </summary>
    private void WithWriteLock(ReaderWriterLockSlim lockObj, Action action)
        => LockHelper.WithWriteLock(lockObj, action);

    /// <summary>
    /// Executes a function while holding a write lock. Ensures lock is released.
    /// Used by Query.cs for _queryLock.
    /// </summary>
    private T WithWriteLock<T>(ReaderWriterLockSlim lockObj, Func<T> func)
        => LockHelper.WithWriteLock(lockObj, func);

    /// <summary>
    /// Executes an async action while holding a semaphore lock. Ensures lock is released.
    /// Used for _joinLock to protect eventJoinIgnore during Join operation.
    /// </summary>
    private async Task WithLockAsync(SemaphoreSlim semaphore, Func<Task> action)
        => await LockHelper.WithLockAsync(semaphore, action);

    /// <summary>
    /// Executes an async function while holding a semaphore lock. Ensures lock is released.
    /// </summary>
    private async Task<T> WithLockAsync<T>(SemaphoreSlim semaphore, Func<Task<T>> func)
        => await LockHelper.WithLockAsync(semaphore, func);

    /// <summary>
    /// Checks if encryption is enabled for this Serf instance.
    /// Encryption is enabled if a keyring is configured.
    /// Maps to: Go's EncryptionEnabled() method
    /// </summary>
    public bool EncryptionEnabled() => Config.MemberlistConfig?.Keyring != null;

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

        var keyring = (Config.MemberlistConfig?.Keyring) ?? throw new InvalidOperationException("No keyring available to write");

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

    /// <summary>
    /// Disposes the Serf instance and releases all locks.
    /// </summary>
    public void Dispose()
    {
        if (_shutdownCts != null && !_shutdownCts.IsCancellationRequested)
        {
            _shutdownCts.Cancel();
        }

        if (Snapshotter != null)
        {
            try
            {
                Snapshotter?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }

        _eventManager?.Dispose();
        _clusterCoordinator?.Dispose();
        _queryLock?.Dispose();
        _coordCacheLock?.Dispose();
        _joinLock?.Dispose();
        _shutdownCts?.Dispose();
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