// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Partial implementation for Phase 6 (Delegates) - will be expanded in Phase 9

using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using NSerf.Serf.Events;
using MessagePack;

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
    
    // Event configuration
    internal bool EventJoinIgnore { get; set; }
    internal LamportTime EventMinTime { get; set; }

    // Memberlist integration
    internal Memberlist.Memberlist? Memberlist { get; private set; }
    private Memberlist.Delegates.IEventDelegate? _eventDelegate;

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
    private readonly SemaphoreSlim _joinLock = new(1, 1);       // Protects: eventJoinIgnore during Join operation

    /// <summary>
    /// Internal constructor for testing - use CreateAsync() factory method for production.
    /// </summary>
    internal Serf(Config config, ILogger? logger = null)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Logger = logger;

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

        EventJoinIgnore = false;
        EventMinTime = 0;
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
    /// Returns all known members in the cluster.
    /// Thread-safe read operation using read lock.
    /// </summary>
    public Member[] Members()
    {
        return WithReadLock(_memberLock, () =>
        {
            var members = new List<Member>();
            foreach (var kvp in MemberStates)
            {
                // Convert MemberInfo to Member
                var member = new Member
                {
                    Name = kvp.Key,
                    Status = MemberStatus.Alive, // TODO: Track actual status
                    Tags = new Dictionary<string, string>()
                };
                members.Add(member);
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
                // Convert MemberInfo to Member
                var member = new Member
                {
                    Name = kvp.Key,
                    Status = MemberStatus.Alive, // TODO: Track actual status
                    Tags = new Dictionary<string, string>()
                };
                
                if (member.Status == statusFilter)
                {
                    members.Add(member);
                }
            }
            return members.ToArray();
        });
    }

    // ========== Phase 9.1: Lifecycle Methods ==========

    /// <summary>
    /// Creates a new Serf instance with the given configuration.
    /// Maps to: Go's Create() function
    /// </summary>
    public static Task<Serf> CreateAsync(Config config)
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

        // Phase 9.2: Initialize memberlist with event delegate
        if (config.MemberlistConfig != null)
        {
            // Create event delegate that routes to Serf
            serf._eventDelegate = new SerfEventDelegate(serf);
            config.MemberlistConfig.Events = serf._eventDelegate;
            
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
            
            // Add local node to members list
            // The memberlist will have the local node, so we need to track it in Serf's Members
            var localMember = new MemberInfo
            {
                Name = config.NodeName,
                StatusLTime = 0
            };
            serf.MemberStates[config.NodeName] = localMember;
        }

        // Phase 9.4: Start background tasks (reaper and reconnect)
        serf.StartBackgroundTasks();

        return Task.FromResult(serf);
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
    /// Updates the tags for the local member and broadcasts the change.
    /// Maps to: Go's SetTags() method
    /// </summary>
    public async Task SetTagsAsync(Dictionary<string, string> tags)
    {
        if (tags == null)
            throw new ArgumentNullException(nameof(tags));

        // Update config tags
        Config.Tags = new Dictionary<string, string>(tags);

        // TODO Phase 9.2+: Broadcast tag update to cluster

        await Task.CompletedTask;
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
                    // TODO Phase 9.3+: Implement broadcastJoin
                    Logger?.LogInformation("[Serf] Successfully joined {NumNodes} nodes", numJoined);
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

            // Wait a bit for broadcast to propagate
            await Task.Delay(100);

            // Notify memberlist to gracefully leave
            if (Memberlist != null)
            {
                try
                {
                    var error = await Memberlist.LeaveAsync(TimeSpan.FromSeconds(5));
                    if (error != null)
                    {
                        Logger?.LogWarning("[Serf] Error during memberlist leave: {Error}", error.Message);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[Serf] Failed to leave memberlist");
                }
            }

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
    public Task<bool> RemoveFailedNodeAsync(string nodeName, bool prune = false)
    {
        if (string.IsNullOrEmpty(nodeName))
            throw new ArgumentException("Node name cannot be null or empty", nameof(nodeName));

        // Cannot remove ourselves
        if (nodeName == Config.NodeName)
        {
            throw new InvalidOperationException("Cannot remove local node");
        }

        return Task.FromResult(WithWriteLock(_memberLock, () =>
        {
            // Check if node exists
            if (!MemberStates.ContainsKey(nodeName))
            {
                return false;
            }

            // TODO Phase 9.3+: Check node status (should be failed)
            
            // Remove from member states
            MemberStates.Remove(nodeName);

            // TODO Phase 9.3+: If prune, remove from snapshot
            if (prune)
            {
                Logger?.LogInformation("[Serf] Pruned failed node: {NodeName}", nodeName);
            }

            Logger?.LogInformation("[Serf] Removed failed node: {NodeName}", nodeName);
            return true;
        }));
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
        // Metrics recording - to be implemented with metrics system
        Logger?.LogTrace("[Serf] Message received: {Size} bytes", size);
    }

    internal void RecordMessageSent(int size)
    {
        // Metrics recording - to be implemented with metrics system
        Logger?.LogTrace("[Serf] Message sent: {Size} bytes", size);
    }

    internal bool HandleNodeLeaveIntent(MessageLeave leave)
    {
        Logger?.LogDebug("[Serf] HandleNodeLeaveIntent: {Node} at LTime {LTime}", leave.Node, leave.LTime);

        // Witness the Lamport time
        Clock.Witness(leave.LTime);

        WithWriteLock(_memberLock, () =>
        {
            // Mark member as leaving if it exists
            if (MemberStates.TryGetValue(leave.Node, out var memberInfo))
            {
                // Only update if this is newer than current status
                if (leave.LTime > memberInfo.StatusLTime)
                {
                    memberInfo.Status = MemberStatus.Leaving;
                    memberInfo.StatusLTime = leave.LTime;
                    Logger?.LogDebug("[Serf] Member {Node} marked as leaving", leave.Node);
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
        // Stub - to be fully implemented in Phase 9
        Logger?.LogDebug("[Serf] HandleNodeJoinIntent: {Node} at LTime {LTime}", join.Node, join.LTime);
        return false; // No rebroadcast for now
    }

    internal bool HandleUserEvent(MessageUserEvent userEvent)
    {
        // Stub - to be fully implemented in Phase 9
        Logger?.LogDebug("[Serf] HandleUserEvent: {Name} at LTime {LTime}", userEvent.Name, userEvent.LTime);
        return false; // No rebroadcast for now
    }

    internal bool HandleQuery(MessageQuery query)
    {
        // Stub - to be fully implemented in Phase 9
        Logger?.LogDebug("[Serf] HandleQuery: {Name}", query.Name);
        return false; // No rebroadcast for now
    }

    internal void HandleQueryResponse(MessageQueryResponse response)
    {
        // Stub - to be fully implemented in Phase 9
        Logger?.LogDebug("[Serf] HandleQueryResponse from: {From}", response.From);
    }

    internal void HandleNodeJoin(Memberlist.State.Node? node)
    {
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeJoin called with null node");
            return;
        }

        Logger?.LogDebug("[Serf] HandleNodeJoin: {Name}", node.Name);

        // Don't process if we're ignoring joins (during rejoin with ignoreOld=true)
        if (EventJoinIgnore)
        {
            Logger?.LogTrace("[Serf] Ignoring join event for: {Name}", node.Name);
            return;
        }

        WithWriteLock(_memberLock, () =>
        {
            // Update or create member state
            if (!MemberStates.TryGetValue(node.Name, out var memberInfo))
            {
                memberInfo = new MemberInfo
                {
                    Name = node.Name,
                    StatusLTime = Clock.Time()
                };
                MemberStates[node.Name] = memberInfo;
                Logger?.LogInformation("[Serf] Member joined: {Name} at {Address}:{Port}", 
                    node.Name, node.Addr, node.Port);
            }
            else
            {
                // Update existing member (rejoin)
                memberInfo.StatusLTime = Clock.Time();
                Logger?.LogInformation("[Serf] Member rejoined: {Name}", node.Name);
            }
        });

        // Emit join event if EventCh is configured
        if (Config.EventCh != null)
        {
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

            try
            {
                Config.EventCh.TryWrite(memberEvent);
                Logger?.LogTrace("[Serf] Emitted join event for: {Name}", node.Name);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf] Failed to emit join event for: {Name}", node.Name);
            }
        }
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

        Logger?.LogDebug("[Serf] HandleNodeLeave: {Name}", node.Name);

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

        // Emit event if EventCh is configured
        if (Config.EventCh != null)
        {
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

            try
            {
                Config.EventCh.TryWrite(memberEvent);
                Logger?.LogTrace("[Serf] Emitted {EventType} event for: {Name}", eventType, node.Name);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf] Failed to emit {EventType} event for: {Name}", eventType, node.Name);
            }
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

        WithWriteLock(_memberLock, () =>
        {
            // Update member metadata if it exists
            if (MemberStates.TryGetValue(node.Name, out var memberInfo))
            {
                memberInfo.StatusLTime = Clock.Time();
                Logger?.LogInformation("[Serf] Member updated: {Name}", node.Name);
            }
        });

        // Emit update event if EventCh is configured
        if (Config.EventCh != null)
        {
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
                Type = EventType.MemberUpdate,
                Members = new List<Member> { member }
            };

            try
            {
                Config.EventCh.TryWrite(memberEvent);
                Logger?.LogTrace("[Serf] Emitted update event for: {Name}", node.Name);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[Serf] Failed to emit update event for: {Name}", node.Name);
            }
        }
    }

    internal void HandleNodeConflict(Memberlist.State.Node? existing, Memberlist.State.Node? other)
    {
        // Stub - to be fully implemented in Phase 9
        if (existing == null || other == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeConflict called with null node(s)");
            return;
        }
        Logger?.LogWarning("[Serf] HandleNodeConflict: {Existing} conflicts with {Other}", 
            existing.Name, other.Name);
    }

    internal Coordinate.Coordinate GetCoordinate()
    {
        // Stub - returns a default coordinate until Phase 9
        // Coordinate system was implemented in Phase 5
        return new Coordinate.Coordinate();
    }

    internal void UpdateCoordinate(string nodeName, Coordinate.Coordinate coordinate, TimeSpan rtt)
    {
        // Stub - to be fully implemented in Phase 9
        Logger?.LogTrace("[Serf] UpdateCoordinate for {Node}, RTT: {RTT}ms", 
            nodeName, rtt.TotalMilliseconds);
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
        // TODO: Phase 9 - Get actual member count from memberlist
        // For now, use a reasonable default
        int n = 1; // Will be: Memberlist.NumMembers()
        var timeout = Config.MemberlistConfig?.GossipInterval ?? TimeSpan.FromMilliseconds(500);
        timeout *= Config.QueryTimeoutMult;
        timeout *= Math.Ceiling(Math.Log10(n + 1));
        return timeout;
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

    /// <summary>
    /// ShouldProcessQuery checks if a query should be processed given a set of filters.
    /// </summary>
    public bool ShouldProcessQuery(List<byte[]> filters)
    {
        foreach (var filter in filters)
        {
            if (filter.Length == 0) continue;

            var filterType = (FilterType)filter[0];

            switch (filterType)
            {
                case FilterType.Node:
                    // Decode the filter
                    string[] nodes;
                    try
                    {
                        var slice = new byte[filter.Length - 1];
                        Array.Copy(filter, 1, slice, 0, filter.Length - 1);
                        nodes = MessagePackSerializer.Deserialize<string[]>(slice);
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogWarning("[Serf] Failed to decode filterNodeType: {Error}", ex.Message);
                        return false;
                    }

                    // Check if we are being targeted
                    if (!nodes.Contains(Config.NodeName))
                    {
                        return false;
                    }
                    break;

                case FilterType.Tag:
                    // Decode the filter
                    FilterTag filt;
                    try
                    {
                        var slice = new byte[filter.Length - 1];
                        Array.Copy(filter, 1, slice, 0, filter.Length - 1);
                        filt = MessagePackSerializer.Deserialize<FilterTag>(slice);
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogWarning("[Serf] Failed to decode filterTagType: {Error}", ex.Message);
                        return false;
                    }

                    // Check if we match this regex
                    var tagValue = Config.Tags.GetValueOrDefault(filt.Tag, string.Empty);
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(tagValue, filt.Expr))
                        {
                            return false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger?.LogWarning("[Serf] Failed to compile filter regex ({Expr}): {Error}", filt.Expr, ex.Message);
                        return false;
                    }
                    break;

                default:
                    Logger?.LogWarning("[Serf] Query has unrecognized filter type: {Type}", filter[0]);
                    return false;
            }
        }

        return true;
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
    public LamportTime StatusLTime { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Alive;
    public DateTimeOffset LeaveTime { get; set; }
    public Member Member { get; set; } = new Member();
}
