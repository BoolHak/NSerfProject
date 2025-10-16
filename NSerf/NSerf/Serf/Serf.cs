// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Partial implementation for Phase 6 (Delegates) - will be expanded in Phase 9

using Microsoft.Extensions.Logging;
using NSerf.Memberlist;
using MessagePack;

namespace NSerf.Serf;

/// <summary>
/// Main Serf class for cluster membership and coordination.
/// Partial implementation supporting Delegate operations - will be fully implemented in Phase 9.
/// </summary>
public class Serf : IDisposable, IAsyncDisposable
{
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

    // Member state
    internal Dictionary<string, MemberInfo> Members { get; private set; }
    internal List<MemberInfo> LeftMembers { get; private set; }
    internal Dictionary<LamportTime, UserEventCollection> EventBuffer { get; private set; }
    
    // Event configuration
    internal bool EventJoinIgnore { get; set; }
    internal LamportTime EventMinTime { get; set; }

    // Memberlist integration
    internal Memberlist.Memberlist? Memberlist { get; private set; }

    // Locks
    private readonly ReaderWriterLockSlim _memberLock = new();
    private readonly ReaderWriterLockSlim _eventLock = new();

    /// <summary>
    /// Constructor for testing and Phase 6 support.
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

        Members = new Dictionary<string, MemberInfo>();
        LeftMembers = new List<MemberInfo>();
        EventBuffer = new Dictionary<LamportTime, UserEventCollection>();

        EventJoinIgnore = false;
        EventMinTime = 0;
    }

    /// <summary>
    /// Returns the number of members known to this Serf instance.
    /// </summary>
    public int NumMembers()
    {
        _memberLock.EnterReadLock();
        try
        {
            return Members.Count;
        }
        finally
        {
            _memberLock.ExitReadLock();
        }
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
        // Stub - to be fully implemented in Phase 9
        Logger?.LogDebug("[Serf] HandleNodeLeaveIntent: {Node} at LTime {LTime}", leave.Node, leave.LTime);
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
        // Stub - to be fully implemented in Phase 9
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeJoin called with null node");
            return;
        }
        Logger?.LogDebug("[Serf] HandleNodeJoin: {Name}", node.Name);
    }

    internal void HandleNodeLeave(Memberlist.State.Node? node)
    {
        // Stub - to be fully implemented in Phase 9
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeLeave called with null node");
            return;
        }
        Logger?.LogDebug("[Serf] HandleNodeLeave: {Name}", node.Name);
    }

    internal void HandleNodeUpdate(Memberlist.State.Node? node)
    {
        // Stub - to be fully implemented in Phase 9
        if (node == null)
        {
            Logger?.LogWarning("[Serf] HandleNodeUpdate called with null node");
            return;
        }
        Logger?.LogDebug("[Serf] HandleNodeUpdate: {Name}", node.Name);
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

    // Lock management
    internal void AcquireMemberLock() => _memberLock.EnterReadLock();
    internal void ReleaseMemberLock() => _memberLock.ExitReadLock();
    internal void AcquireEventLock() => _eventLock.EnterReadLock();
    internal void ReleaseEventLock() => _eventLock.ExitReadLock();

    /// <summary>
    /// Disposes the Serf instance.
    /// </summary>
    public void Dispose()
    {
        _memberLock?.Dispose();
        _eventLock?.Dispose();
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
/// Temporary member info structure for Phase 6.
/// Will be replaced with proper Member class in Phase 9.
/// </summary>
internal class MemberInfo
{
    public string Name { get; set; } = string.Empty;
    public LamportTime StatusLTime { get; set; }
}
